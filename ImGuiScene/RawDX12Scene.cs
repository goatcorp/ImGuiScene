using ImGuiNET;
using PInvoke;
using SharpDX.Direct3D12;
using SharpDX.DXGI;
using StbiSharp;
using System;
using System.ComponentModel;
using System.IO;
using ImGuiScene.ImGui_Impl.Native;
using ImGuizmoNET;
using ImPlotNET;

using Device = SharpDX.Direct3D12.Device;

namespace ImGuiScene {
    // This class will likely eventually be unified a bit more with other scenes, but for
    // now it should be directly useable
    public sealed class RawDX12Scene : IDisposable {
        public Device Device { get; private set; }
        public IntPtr WindowHandlePtr { get; private set; }

        // IDXGISwapChain3 is required to call GetCurrentBackBufferIndex.
        public SwapChain3 SwapChain { get; private set; }
        public CommandQueue CommandQueue { get; private set; }

        public bool UpdateCursor {
            get => this.imguiInput.UpdateCursor;
            set => this.imguiInput.UpdateCursor = value;
        }

        private int targetWidth;
        private int targetHeight;

        private ImGui_ImplDX12_Native imguiRenderer;
        private ImGui_Input_Impl_Direct imguiInput;

        public delegate void BuildUIDelegate();

        public delegate void NewInputFrameDelegate();

        public delegate void NewRenderFrameDelegate();

        /// <summary>
        /// User methods invoked every ImGui frame to construct custom UIs.
        /// </summary>
        public BuildUIDelegate OnBuildUI;

        public NewInputFrameDelegate OnNewInputFrame;
        public NewRenderFrameDelegate OnNewRenderFrame;

        private string imguiIniPath;

        public string ImGuiIniPath {
            get => this.imguiIniPath;
            set {
                this.imguiIniPath = value;
                this.imguiInput.SetIniPath(this.imguiIniPath);
            }
        }

        /// <summary>
        /// Creates an instance of the class <see cref="RawDX12Scene"/>.
        /// </summary>
        /// <param name="nativeSwapChain">Pointer to an IDXGISwapChain.</param>
        /// <param name="nativeCommandQueue">Pointer to the ID3D12CommandQueue used to initialize <see cref="nativeSwapChain"/></param>
        /// <param name="nativeDevice">Pointer to a native ID3D12Device. By default the device will be derived from <param name="nativeSwapChain">.</param></param>
        /// <remarks>
        /// Ensure <see cref="nativeCommandQueue"/> was the command queue used to initialize <see cref="nativeSwapChain"/>, otherwise rendering will crash.
        /// </remarks>
        public RawDX12Scene(IntPtr nativeSwapChain, IntPtr nativeCommandQueue, IntPtr? nativeDevice = null) {
            this.SwapChain = new SwapChain(nativeSwapChain).QueryInterfaceOrNull<SwapChain3>()
                             ?? throw new InvalidEnumArgumentException("Failed to query SwapChain3 interface");
            this.CommandQueue = new CommandQueue(nativeCommandQueue);
            this.Device = nativeDevice is null ? this.SwapChain.GetDevice<Device>() : new Device((IntPtr)nativeDevice);

            this.Initialize();
        }

        private void Initialize() {
            // could also do things with GetClientRect() for WindowHandlePtr, not sure if that is necessary
            this.targetWidth = this.SwapChain.Description.ModeDescription.Width;
            this.targetHeight = this.SwapChain.Description.ModeDescription.Height;

            this.WindowHandlePtr = this.SwapChain.Description.OutputHandle;

            this.InitializeImGui();
        }

        private void InitializeImGui() {
            this.imguiRenderer = new ImGui_ImplDX12_Native();

            var ctx = ImGui.CreateContext();
            ImGuizmo.SetImGuiContext(ctx);
            ImPlot.SetImGuiContext(ctx);
            ImPlot.CreateContext();

            ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.ViewportsEnable;

            this.imguiRenderer.Init(this.Device, this.SwapChain, this.CommandQueue, 1);
            this.imguiInput = new ImGui_Input_Impl_Direct(this.WindowHandlePtr);
        }

        /// <summary>
        /// Processes window messages.
        /// </summary>
        /// <param name="hWnd">Handle of the window.</param>
        /// <param name="msg">Type of window message.</param>
        /// <param name="wParam">wParam.</param>
        /// <param name="lParam">lParam.</param>
        /// <returns>Return value.</returns>
        public unsafe IntPtr? ProcessWndProcW(IntPtr hWnd, User32.WindowMessage msg, void* wParam, void* lParam) {
            return this.imguiInput.ProcessWndProcW(hWnd, msg, wParam, lParam);
        }

        public void Render() {
            this.imguiRenderer.NewFrame();
            this.OnNewRenderFrame?.Invoke();
            this.imguiInput.NewFrame(targetWidth, targetHeight);
            this.OnNewInputFrame?.Invoke();

            ImGui.NewFrame();
            ImGuizmo.BeginFrame();

            this.OnBuildUI?.Invoke();

            ImGui.Render();

            this.imguiRenderer.RenderDrawData(ImGui.GetDrawData());
            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
        }

        /// <summary>
        /// This should be called after the swapchain present call has finished.
        /// </summary>
        public void OnPostPresent() {
            this.imguiRenderer.OnPostPresent();
        }

        public void OnPreResize() {
            this.imguiRenderer.InvalidateRenderTargets();
        }

        // TODO: If resize buffers is never called we don't initialize the render view targets, maybe add OnPostGetBuffer?
        public void OnPostResize(int bufferCount, int newWidth, int newHeight, int newFormat) {
            this.imguiRenderer.CreateRenderTargets(bufferCount);

            this.targetWidth = newWidth;
            this.targetHeight = newHeight;
        }

        // It is pretty much required that this is called from a handler attached
        // to OnNewRenderFrame
        public void InvalidateFonts() {
            // TODO
            this.imguiRenderer.RebuildFontTexture();
        }

        // It is pretty much required that this is called from a handler attached
        // to OnNewRenderFrame
        public void ClearStacksOnContext() {
            // TODO: Crashes. I don't think this should be used here.
            // Custom.igCustom_ClearStacks();
        }

        public bool IsImGuiCursor(IntPtr hCursor) {
            return this.imguiInput.IsImGuiCursor(hCursor);
        }

        public TextureWrap LoadImage(string path) {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var ms = new MemoryStream();
            fs.CopyTo(ms);
            var image = Stbi.LoadFromMemory(ms, 4);
            return this.LoadImage_Internal(image);
        }

        public TextureWrap LoadImage(byte[] imageBytes) {
            using var ms = new MemoryStream(imageBytes, 0, imageBytes.Length, false, true);
            var image = Stbi.LoadFromMemory(ms, 4);
            return this.LoadImage_Internal(image);
        }

        public unsafe TextureWrap LoadImageRaw(byte[] imageData, int width, int height, int numChannels = 4) {
            // StbiSharp doesn't expose a constructor, even just to wrap existing data, which means
            // short of something awful like below, or creating another wrapper layer, we can't avoid
            // adding divergent code paths into CreateTexture
            //var mock = new { Width = width, Height = height, NumChannels = numChannels, Data = imageData };
            //var image = Unsafe.As<StbiImage>(mock);
            //return LoadImage_Internal(image);

            fixed (void* pixelData = imageData) {
                return this.CreateTexture(pixelData, width, height, numChannels);
            }
        }

        private unsafe TextureWrap LoadImage_Internal(StbiImage image) {
            fixed (void* pixelData = image.Data) {
                return this.CreateTexture(pixelData, image.Width, image.Height, image.NumChannels);
            }
        }

        private unsafe TextureWrap CreateTexture(void* pixelData, int width, int height, int bytesPerPixel) {
            return this.imguiRenderer.CreateTexture(pixelData, width, height, bytesPerPixel);
        }

        public byte[] CaptureScreenshot() {
            throw new NotImplementedException();
            // using (var backBuffer = this.SwapChain.GetBackBuffer<Texture2D>(0))
            // {
            //     Texture2DDescription desc = backBuffer.Description;
            //     desc.CpuAccessFlags = CpuAccessFlags.Read;
            //     desc.Usage = ResourceUsage.Staging;
            //     desc.OptionFlags = ResourceOptionFlags.None;
            //     desc.BindFlags = BindFlags.None;
            //
            //     using (var tex = new Texture2D(this.Device, desc))
            //     {
            //         this.deviceContext.CopyResource(backBuffer, tex);
            //         using (var surf = tex.QueryInterface<Surface>())
            //         {
            //             var map = surf.Map(SharpDX.DXGI.MapFlags.Read, out DataStream dataStream);
            //             var pixelData = new byte[surf.Description.Width * surf.Description.Height * surf.Description.Format.SizeOfInBytes()];
            //             var dataCounter = 0;
            //
            //             while (dataCounter < pixelData.Length)
            //             {
            //                 //var curPixel = dataStream.Read<uint>();
            //                 var x = dataStream.Read<byte>();
            //                 var y = dataStream.Read<byte>();
            //                 var z = dataStream.Read<byte>();
            //                 var w = dataStream.Read<byte>();
            //
            //                 pixelData[dataCounter++] = z;
            //                 pixelData[dataCounter++] = y;
            //                 pixelData[dataCounter++] = x;
            //                 pixelData[dataCounter++] = w;
            //             }
            //
            //             // TODO: test this on a thread
            //             //var gch = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
            //             //using (var bitmap = new Bitmap(surf.Description.Width, surf.Description.Height, map.Pitch, PixelFormat.Format32bppRgb, gch.AddrOfPinnedObject()))
            //             //{
            //             //    bitmap.Save(path);
            //             //}
            //             //gch.Free();
            //
            //             surf.Unmap();
            //             dataStream.Dispose();
            //
            //             return pixelData;
            //         }
            //     }
            // }
        }

        #region IDisposable Support

        private bool disposedValue; // To detect redundant calls

        private void Dispose(bool disposing) {
            if (this.disposedValue) return;

            if (disposing) {
                // TODO: dispose managed state (managed objects).
            }

            this.imguiRenderer?.Shutdown();
            this.imguiInput?.Dispose();

            // TODO: Crashes. Possible double free?
            // ImGui.DestroyContext();

            this.imguiRenderer?.Dispose();

            // Not actually sure how sharpdx does ref management, but hopefully they
            // addref when we create our wrappers, so this should just release that count

            // Originally it was thought these lines were needed because it was assumed that SharpDX does
            // proper refcounting to handle disposing, but disposing these would cause the game to crash
            // on resizing after unloading Dalamud
            // this.SwapChain?.Dispose();
            // this.deviceContext?.Dispose();
            // this.Device?.Dispose();

            this.disposedValue = true;
        }

        ~RawDX12Scene() {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose() {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
