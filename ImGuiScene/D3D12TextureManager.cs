using System;
using System.Collections.Generic;
using System.Diagnostics;
using SharpDX.Direct3D12;
using SharpDX.DXGI;

using Device = SharpDX.Direct3D12.Device;
using Resource = SharpDX.Direct3D12.Resource;

using static ImGuiScene.NativeMethods;

namespace ImGuiScene {
    /// <summary>
    /// Class that manages the creation, lookup, and synchronization of textures as <see cref="D3D12TextureWrap"/>.
    /// </summary>
    public class D3D12TextureManager : IDisposable {
        private readonly Device _device;
        private volatile bool _disposed;

        private TextureInfo[] _staticBoundTextures;
        private readonly Dictionary<TextureInfo, GpuDescriptorHandle> _dynamicBoundTextures = new();
        private readonly object _textureLock = new();
        private readonly int _staticTextureCount;
        private readonly int _descriptorCount;

        public D3D12TextureManager(Device device, int staticTextureCount, int maxDynamicTexturesPerFrame = 1023) {
            this._device = device;
            this._staticBoundTextures = new TextureInfo[staticTextureCount];
            this._descriptorCount = staticTextureCount + maxDynamicTexturesPerFrame;
            this._staticTextureCount = staticTextureCount;

            this.CbvSrvHeap = this._device.CreateDescriptorHeap(new DescriptorHeapDescription {
                Type = DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                DescriptorCount = this._descriptorCount,
                Flags = DescriptorHeapFlags.ShaderVisible
            });
        }

        public DescriptorHeap CbvSrvHeap { get; }

        // TODO: Use a LRU cache to swap out textures instead of clearing the dynamic texture binds each frame.
        public void ClearDynamicTextures() {
            lock (this._textureLock) {
                this._dynamicBoundTextures.Clear();
            }
        }

        public void BindStaticTexture(D3D12TextureWrap wrap, int index) {
            Debug.Assert(index < this._staticTextureCount, "index < this._staticTextureCount");

            lock (this._textureLock) {
                var (cpuHandle, _) = this.GetSrvHandles(index);
                this._device.CreateShaderResourceView(wrap.Info.Resource, wrap.Info.SrvDesc, cpuHandle);

                this._staticBoundTextures[index]?.Dispose();
                this._staticBoundTextures[index] = wrap.Info;
            }
        }

        public unsafe D3D12TextureWrap CreateTexture(void* pixelData, int width, int height, int bytesPerPixel,
            Format format) {
            // TODO: Figure out if we need to implement other sizes.
            Debug.Assert(bytesPerPixel == 4, "bytesPerPixel == 4");

            // Upload texture to graphics system
            var props = new HeapProperties {
                Type = HeapType.Default,
                CPUPageProperty = CpuPageProperty.Unknown,
                MemoryPoolPreference = MemoryPool.Unknown
            };

            var desc = new ResourceDescription {
                Dimension = ResourceDimension.Texture2D,
                Alignment = 0,
                Width = width,
                Height = height,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = format,
                SampleDescription = { Count = 1, Quality = 0 },
                Layout = TextureLayout.Unknown,
                Flags = ResourceFlags.None
            };

            var texture =
                this._device.CreateCommittedResource(props, HeapFlags.None, desc, ResourceStates.CopyDestination);

            var uploadPitch = (width * bytesPerPixel + D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1u) &
                              ~(D3D12_TEXTURE_DATA_PITCH_ALIGNMENT - 1u);
            var uploadSize = height * uploadPitch;
            desc.Dimension = ResourceDimension.Buffer;
            desc.Alignment = 0;
            desc.Width = uploadSize;
            desc.Height = 1;
            desc.DepthOrArraySize = 1;
            desc.MipLevels = 1;
            desc.Format = Format.Unknown;
            desc.SampleDescription.Count = 1;
            desc.SampleDescription.Quality = 0;
            desc.Layout = TextureLayout.RowMajor;
            desc.Flags = ResourceFlags.None;

            props.Type = HeapType.Upload;
            props.CPUPageProperty = CpuPageProperty.Unknown;
            props.MemoryPoolPreference = MemoryPool.Unknown;

            var uploadBuffer =
                this._device.CreateCommittedResource(props, HeapFlags.None, desc, ResourceStates.GenericRead);

            var range = new Range { Begin = 0, End = uploadSize };
            var mapped = uploadBuffer.Map(0, range);

            var lenToCopy = width * bytesPerPixel;
            for (var y = 0; y < height; y++) {
                Buffer.MemoryCopy((void*)((IntPtr)pixelData + y * width * bytesPerPixel),
                    (void*)(mapped + y * (nint)uploadPitch), lenToCopy, lenToCopy);
            }

            uploadBuffer.Unmap(0, range);

            var srcLocation = new TextureCopyLocation(uploadBuffer, new PlacedSubResourceFootprint {
                Footprint = new SubResourceFootprint {
                    Format = format,
                    Width = width,
                    Height = height,
                    Depth = 1,
                    RowPitch = (int)uploadPitch,
                }
            });

            var dstLocation = new TextureCopyLocation(texture, 0);

            var barrier = new ResourceBarrier {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Transition = new ResourceTransitionBarrier(texture, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES,
                    ResourceStates.CopyDestination, ResourceStates.PixelShaderResource)
            };

            var fence = this._device.CreateFence(0, FenceFlags.None);

            var ev = CreateEvent(IntPtr.Zero, false, false, null);
            Debug.Assert(ev != IntPtr.Zero, "ev != IntPtr.Zero");

            var queueDesc = new CommandQueueDescription {
                Type = CommandListType.Direct,
                Flags = CommandQueueFlags.None,
                NodeMask = 1
            };

            var cmdQueue = this._device.CreateCommandQueue(queueDesc);
            var cmdAlloc = this._device.CreateCommandAllocator(CommandListType.Direct);
            var cmdList = this._device.CreateCommandList(0, CommandListType.Direct, cmdAlloc, null);

            cmdList.CopyTextureRegion(dstLocation, 0, 0, 0, srcLocation, null);
            cmdList.ResourceBarrier(barrier);
            cmdList.Close();

            cmdQueue.ExecuteCommandLists(cmdList);
            cmdQueue.Signal(fence, 1);

            fence.SetEventOnCompletion(1, ev);
            WaitForSingleObject(ev, INFINITE);

            cmdList.Dispose();
            cmdAlloc.Dispose();
            cmdQueue.Dispose();
            CloseHandle(ev);
            fence.Dispose();
            uploadBuffer.Dispose();

            // Create texture view
            var srvDesc = new ShaderResourceViewDescription {
                Format = format,
                Dimension = ShaderResourceViewDimension.Texture2D,
                Texture2D = { MipLevels = desc.MipLevels, MostDetailedMip = 0 },
                Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING
            };

            // no sampler for now because the ImGui implementation we copied doesn't allow for changing it
            return new D3D12TextureWrap(new TextureInfo {
                Resource = texture,
                SrvDesc = srvDesc,
            }, width, height, this.AwaitTextureDispose, this.GetOrBindTextureData);
        }

        public void Dispose() {
            this._disposed = true;

            foreach (var texture in this._staticBoundTextures) {
                texture.Dispose();
            }

            this._staticBoundTextures = null;
        }

        private (CpuDescriptorHandle, GpuDescriptorHandle) GetSrvHandles(int textureIndex) {
            var srvHandleSize = this._device.GetDescriptorHandleIncrementSize(DescriptorHeapType
                .ConstantBufferViewShaderResourceViewUnorderedAccessView);

            var cpuHandle = this.CbvSrvHeap.CPUDescriptorHandleForHeapStart;
            cpuHandle.Ptr += textureIndex * srvHandleSize;

            var gpuHandle = this.CbvSrvHeap.GPUDescriptorHandleForHeapStart;
            gpuHandle.Ptr += textureIndex * srvHandleSize;

            return (cpuHandle, gpuHandle);
        }

        private void AwaitTextureDispose(D3D12TextureWrap wrap) {
            lock (this._textureLock) {
                wrap.Info.Resource.Dispose();
                wrap.Info = null;
            }
        }

        private IntPtr GetOrBindTextureData(D3D12TextureWrap wrap) {
            if (this._disposed) return IntPtr.Zero;

            lock (this._textureLock) {
                if (this._dynamicBoundTextures.TryGetValue(wrap.Info, out var handle)) {
                    return (IntPtr)handle.Ptr;
                }

                for (var i = 0; i < this._staticBoundTextures.Length; i++) {
                    var info = this._staticBoundTextures[i];
                    if (info == wrap.Info) {
                        return (IntPtr)this.GetSrvHandles(i).Item2.Ptr;
                    }
                }

                var nextDescriptorIndex = this._staticBoundTextures.Length + this._dynamicBoundTextures.Count;
                if (nextDescriptorIndex >= this._descriptorCount) {
                    throw new OutOfMemoryException("Ran out of heap descriptors");
                }

                var (cpuHandle, gpuHandle) = this.GetSrvHandles(nextDescriptorIndex);
                this._device.CreateShaderResourceView(wrap.Info.Resource, wrap.Info.SrvDesc, cpuHandle);
                this._dynamicBoundTextures.Add(wrap.Info, gpuHandle);
                return (IntPtr)gpuHandle.Ptr;
            }
        }

        internal record TextureInfo : IDisposable {
            public Resource Resource;
            public ShaderResourceViewDescription SrvDesc;

            public void Dispose() {
                this.Resource.Dispose();
            }
        }
    }
}
