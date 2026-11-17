// SPDX-License-Identifier: MIT. Playhub, Copyright (c) 2026 Andrea Sgarro (LoZazaMastro).
namespace Playhub.StoreLibrary;

// File sharing locks are process-death-safe and have no thread-affine mutex release.
internal sealed class StoreNativeOperationGate(string path)
{
    private readonly SemaphoreSlim local = new(1, 1);
    private FileStream? retained;
    internal bool IsHeld => local.CurrentCount == 0;
    public bool TryWait()
    {
        if (!local.Wait(0)) return false;
        try { retained = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { local.Release(); return false; }
        catch { local.Release(); throw; }
    }
    public async Task WaitAsync(CancellationToken token)
    {
        await local.WaitAsync(token);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { retained = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return; }
                catch (IOException) { await Task.Delay(50, token); }
            }
        }
        catch { local.Release(); throw; }
    }
    public void Release() { retained?.Dispose(); retained = null; local.Release(); }
}
