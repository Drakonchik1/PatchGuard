namespace PatchGuard.Services.Platform;

public interface IUserConfirmationService
{
    bool Confirm(string title, string message);
}
