using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ImGuiNET;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace ImGuiScene.ImGui_Impl.Native {
    public unsafe class ImGui_ImplDX11_Native {
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool igImpls_ImplDX11_Init(IntPtr device, IntPtr deviceContext);
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX11_Shutdown();
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX11_NewFrame();
        
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX11_RenderDrawData(ImDrawData* drawData);
        
        // Only needed to support recreating the device, doubt we need this ever
        /*
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igImpls_ImplDX11_InvalidateDeviceObjects();
        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool igImpls_ImplDX11_CreateDeviceObjects();
        */

        private Device device;
        private List<ShaderResourceView> _fontResourceViews = new();
        private SamplerState _fontSampler;

        public void Init(Device device, DeviceContext deviceContext) {
            this.device = device;
            
            var init = igImpls_ImplDX11_Init(device.NativePointer, deviceContext.NativePointer);
            Debug.Assert(init, "Couldn't init native dx11 backend");
        }

        public void NewFrame() {
            igImpls_ImplDX11_NewFrame();
        }

        public void RenderDrawData(ImDrawDataPtr drawDataPtr) {
            igImpls_ImplDX11_RenderDrawData(drawDataPtr.NativePtr);
        }

        public void Shutdown() {
            igImpls_ImplDX11_Shutdown();
        }
        
        // Added to support dynamic rebuilding of the font texture
        // for adding fonts after initialization time
        public void RebuildFontTexture()
        {
            _fontSampler?.Dispose();
            foreach (var fontResourceView in this._fontResourceViews)
                fontResourceView.Dispose();
            this._fontResourceViews.Clear();

            CreateFontsTexture();
        }
        
        private void CreateFontsTexture()
        {
            var io = ImGui.GetIO();
            if (io.Fonts.Textures.Size == 0)
                io.Fonts.Build();
            
            for (int textureIndex = 0, textureCount = io.Fonts.Textures.Size;
                 textureIndex < textureCount;
                 textureIndex++) {

                // Build texture atlas
                io.Fonts.GetTexDataAsRGBA32(textureIndex, out IntPtr fontPixels, out int fontWidth, out int fontHeight,
                                            out int fontBytesPerPixel);

                // Upload texture to graphics system
                var texDesc = new Texture2DDescription {
                    Width = fontWidth,
                    Height = fontHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.R8G8B8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.ShaderResource,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None
                };

                using var fontTexture = new Texture2D(
                    this.device, texDesc, new DataRectangle(fontPixels, fontWidth * fontBytesPerPixel));

                // Create texture view
                var fontResourceView = new ShaderResourceView(this.device, fontTexture, new ShaderResourceViewDescription {
                    Format = texDesc.Format,
                    Dimension = ShaderResourceViewDimension.Texture2D,
                    Texture2D = { MipLevels = texDesc.MipLevels }
                });

                // Store our identifier
                _fontResourceViews.Add(fontResourceView);
                io.Fonts.SetTexID(textureIndex, fontResourceView.NativePointer);
            }

            io.Fonts.ClearTexData();

            // Create texture sampler
            _fontSampler = new SamplerState(this.device, new SamplerStateDescription
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Wrap,
                AddressV = TextureAddressMode.Wrap,
                AddressW = TextureAddressMode.Wrap,
                MipLodBias = 0,
                ComparisonFunction = Comparison.Always,
                MinimumLod = 0,
                MaximumLod = 0
            });
        }
    }
}
