using Luma.Models;

namespace Luma.Services;

public sealed class ApprovalPolicy
{
    public ApprovalDecision Evaluate(SensitiveAction action)
    {
        return action switch
        {
            SensitiveAction.Read => new(false, "Read-only action"),
            SensitiveAction.Navigate => new(false, "Normal browser navigation"),
            SensitiveAction.Download => new(false, "Download does not commit an external action"),
            SensitiveAction.SendEmail => new(true, "Sending a message acts as the user"),
            SensitiveAction.CreateCalendarEvent => new(true, "Creates or changes calendar data"),
            SensitiveAction.ConfirmReservation => new(true, "Confirms a reservation with an external service"),
            SensitiveAction.Purchase => new(true, "Commits money or a payment method"),
            SensitiveAction.DeleteData => new(true, "Destructive action"),
            SensitiveAction.GitPush => new(true, "Publishes source changes"),
            SensitiveAction.ExecuteShell => new(true, "Can modify the local computer"),
            _ => new(true, "Unknown action defaults to approval required")
        };
    }
}
