using ImGuiScene;
using SharpDX.Direct3D11;
using System;

/// <summary>
/// DX11 Implementation of <see cref="TextureWrap"/>.
/// Provides a simple wrapped view of the disposable resource as well as the handle for ImGui.
/// </summary>
public class D3D11TextureWrap : TextureWrap {
    // hold onto this directly for easier dispose etc and in case we need it later
    private ShaderResourceView _resourceView;

    public int Width { get; }
    public int Height { get; }
    public IntPtr ImGuiHandle => _resourceView?.NativePointer ?? IntPtr.Zero;

    public D3D11TextureWrap(ShaderResourceView texView, int width, int height) {
        _resourceView = texView;
        Width = width;
        Height = height;
    }

    #region IDisposable Support

    private bool disposedValue; // To detect redundant calls

    protected virtual void Dispose(bool disposing) {
        if (!disposedValue) {
            if (disposing) {
                // TODO: dispose managed state (managed objects).
            }

            // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
            // TODO: set large fields to null.

            _resourceView?.Dispose();
            _resourceView = null;

            disposedValue = true;
        }
    }

    ~D3D11TextureWrap() {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(false);
    }

    public void Dispose() {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
