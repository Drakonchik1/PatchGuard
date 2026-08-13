using PatchGuard.Services.Platform;

namespace PatchGuard.Tests;

internal sealed class StubUserConfirmationService(bool confirm = false) : IUserConfirmationService
{
    public bool Confirm(string title, string message) => confirm;
}
