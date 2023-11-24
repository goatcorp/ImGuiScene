using System;

namespace ImGuiScene {
    /// <summary>
    /// DX12 Implementation of <see cref="TextureWrap"/>.
    /// Provides a simple wrapped view of the disposable resource as well as the handle for ImGui.
    /// </summary>
    public class D3D12TextureWrap : TextureWrap {
        private readonly Action<D3D12TextureWrap> _disposing;
        private readonly Func<D3D12TextureWrap, IntPtr> _getImGuiHandle;

        internal D3D12TextureManager.TextureInfo Info { get; set; }

        public int Width { get; }
        public int Height { get; }
        public IntPtr ImGuiHandle => this._getImGuiHandle(this);

        internal D3D12TextureWrap(D3D12TextureManager.TextureInfo info, int width, int height,
            Action<D3D12TextureWrap> disposing,
            Func<D3D12TextureWrap, IntPtr> getImGuiHandle) {
            this.Info = info;
            this.Width = width;
            this.Height = height;
            this._disposing = disposing;
            this._getImGuiHandle = getImGuiHandle;
        }

        #region IDisposable Support

        private bool _disposedValue; // To detect redundant calls

        protected virtual void Dispose(bool disposing) {
            if (this._disposedValue) return;

            if (disposing) {
                this._disposing(this);
            }

            // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
            // TODO: set large fields to null.

            this._disposedValue = true;
        }

        ~D3D12TextureWrap() {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(false);
        }

        public void Dispose() {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
