using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SharpDX.Direct3D12;
using SharpDX.DXGI;

using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

using static ImGuiScene.NativeMethods;

namespace ImGuiScene.ImGui_Impl.Native {
    public unsafe class ImGui_ImplDX12_Native : IDisposable {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool igImpls_ImplDX12_Init(IntPtr device, int numFramesInFlight, Format rtvFormat,
            IntPtr cbvSrvHeap, CpuDescriptorHandle fontSrvCpuDescHandle, GpuDescriptorHandle fontSrvGpuDescHandle);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX12_Shutdown();

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX12_NewFrame();

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX12_RenderDrawData(ImDrawData* drawData, IntPtr graphicsCommandList);

        // Only needed to support recreating the device, doubt we need this ever
        /*
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX12_InvalidateDeviceObjects();
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool igImpls_ImplDX12_CreateDeviceObjects();
        */

        private Device _device;
        private SwapChain3 _swapChain;

        // TODO: Detect format from render target?
        private const Format DXGI_FORMAT = Format.R8G8B8A8_UNorm;

        private D3D12TextureManager _textureManager;
        private List<TextureWrap> _fontTextures = new();
        private DescriptorHeap _rtvDescriptorHeap;
        private readonly List<(Resource, CpuDescriptorHandle)> _mainRtv = new();

        private CommandQueue _commandQueue;
        private CommandAllocator _commandAllocator;
        private GraphicsCommandList _commandList;

        private Fence _fence;
        private IntPtr _fenceEvent;
        private long _fenceValue;

        public void Init(Device device, SwapChain3 swapChain, CommandQueue commandQueue, int numFramesInFlight) {
            this._device = device;
            this._swapChain = swapChain;
            // Reserve one static descriptor for the ImGui font.
            // TODO: Make this work for multiple ImGui fonts?
            this._textureManager = new D3D12TextureManager(device, 1);

            var init = igImpls_ImplDX12_Init(
                device.NativePointer,
                numFramesInFlight,
                DXGI_FORMAT,
                this._textureManager.CbvSrvHeap.NativePointer,
                this._textureManager.CbvSrvHeap.CPUDescriptorHandleForHeapStart,
                this._textureManager.CbvSrvHeap.GPUDescriptorHandleForHeapStart
            );
            Debug.Assert(init, "Couldn't init native dx12 backend");

            this._commandQueue = commandQueue;
            this._commandAllocator = this._device.CreateCommandAllocator(CommandListType.Direct);
            this._commandList = this._device.CreateCommandList(CommandListType.Direct, this._commandAllocator, null);
            this._commandList.Close();

            this._fence = this._device.CreateFence(this._fenceValue, FenceFlags.None);
            this._fenceEvent = CreateEvent(IntPtr.Zero, false, false, null);
        }

        public void NewFrame() {
            this._textureManager.ClearDynamicTextures();
            igImpls_ImplDX12_NewFrame();
        }

        public void RenderDrawData(ImDrawDataPtr drawDataPtr) {
            if (this._mainRtv.Count == 0)
                return;

            var backBufferIndex = this._swapChain.CurrentBackBufferIndex;
            this._commandAllocator.Reset();

            var barrier = new ResourceBarrier {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Transition = new ResourceTransitionBarrier(this._mainRtv[backBufferIndex].Item1,
                    D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, ResourceStates.Present, ResourceStates.RenderTarget),
            };
            this._commandList.Reset(this._commandAllocator, null);
            this._commandList.ResourceBarrier(barrier);

            this._commandList.SetRenderTargets(this._mainRtv[backBufferIndex].Item2, null);
            // TODO: Idk if this is necessary since ImGui might do it? Test without it.
            this._commandList.SetDescriptorHeaps(this._textureManager.CbvSrvHeap);

            igImpls_ImplDX12_RenderDrawData(drawDataPtr.NativePtr, this._commandList.NativePointer);

            barrier.Transition = new ResourceTransitionBarrier(this._mainRtv[backBufferIndex].Item1,
                D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, ResourceStates.RenderTarget, ResourceStates.Present);
            this._commandList.ResourceBarrier(barrier);
            this._commandList.Close();

            this._commandQueue.ExecuteCommandLists(this._commandList);
        }

        public void Shutdown() {
            this.WaitForLastSubmittedFrame();
            igImpls_ImplDX12_Shutdown();
        }

        public void OnPostPresent() {
            var fenceValue = this._fenceValue + 1;
            this._commandQueue.Signal(this._fence, fenceValue);
            this._fenceValue = fenceValue;
        }

        public void InvalidateRenderTargets() {
            foreach (var (resource, _) in this._mainRtv) {
                resource.Dispose();
            }

            this._mainRtv.Clear();
            this._rtvDescriptorHeap?.Dispose();
            this._rtvDescriptorHeap = null;
        }

        public void CreateRenderTargets(int bufferCount) {
            this.InvalidateRenderTargets();

            var desc = new DescriptorHeapDescription {
                Type = DescriptorHeapType.RenderTargetView,
                DescriptorCount = bufferCount,
                Flags = DescriptorHeapFlags.None,
                NodeMask = 1
            };

            this._rtvDescriptorHeap = this._device.CreateDescriptorHeap(desc);

            var rtvDescriptorSize =
                this._device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            var rtvHandle = this._rtvDescriptorHeap.CPUDescriptorHandleForHeapStart;

            for (var i = 0; i < bufferCount; i++) {
                var backBuffer = this._swapChain.GetBackBuffer<Resource>(i);

                Debug.Assert(backBuffer is not null, "backBuffer was null in CreateRenderTargets");

                this._device.CreateRenderTargetView(backBuffer, null, rtvHandle);
                this._mainRtv.Add((backBuffer, rtvHandle));

                rtvHandle.Ptr += rtvDescriptorSize;
            }
        }

        public TextureWrap CreateTexture(void* pixelData, int width, int height, int bytesPerPixel) {
            return this._textureManager.CreateTexture(pixelData, width, height, bytesPerPixel, DXGI_FORMAT);
        }

        // Added to support dynamic rebuilding of the font texture
        // for adding fonts after initialization time
        public void RebuildFontTexture() {
            foreach (var font in this._fontTextures) {
                font.Dispose();
            }

            this._fontTextures.Clear();
            this.CreateFontsTexture();
        }

        private void WaitForLastSubmittedFrame() {
            var fenceValue = this._fenceValue;
            if (fenceValue == 0)
                return; // No fence was signaled

            this._fenceValue = 0;
            if (this._fence.CompletedValue >= fenceValue)
                return;

            this._fence.SetEventOnCompletion(fenceValue, this._fenceEvent);
            WaitForSingleObject(this._fenceEvent, INFINITE);
        }

        private void CreateFontsTexture() {
            var io = ImGui.GetIO();
            if (io.Fonts.Textures.Size == 0)
                io.Fonts.Build();

            for (int textureIndex = 0, textureCount = io.Fonts.Textures.Size;
                 textureIndex < textureCount;
                 textureIndex++) {
                // Build texture atlas
                io.Fonts.GetTexDataAsRGBA32(textureIndex, out IntPtr fontPixels, out var fontWidth,
                    out var fontHeight,
                    out var fontBytesPerPixel);

                var wrap = this._textureManager.CreateTexture((void*)fontPixels, fontWidth, fontHeight,
                    fontBytesPerPixel, DXGI_FORMAT);
                // TODO: Multiple fonts?
                this._textureManager.BindStaticTexture(wrap, textureIndex);

                this._fontTextures.Add(wrap);
                // TODO: I'm pretty sure this just overrides the global font over and over. Verify.
                io.Fonts.SetTexID(textureIndex, wrap.ImGuiHandle);
            }

            io.Fonts.ClearTexData();
        }

        public void Dispose() {
            CloseHandle(this._fenceEvent);
            this._fence.Dispose();
            this.InvalidateRenderTargets();
            this._fontTextures.Clear();
            this._textureManager.Dispose();
        }
    }
}
