namespace CAV.BillingView.Models;

public class ControlTowerEvent
{
    public required string EventVersion { get; set; }
    public required DateTime EventTime { get; set; }
    public required string EventSource { get; set; }
    public required string EventName { get; set; }
    public required string AwsRegion { get; set; }
    public required string SourceIpAddress { get; set; }
    public required string UserAgent { get; set; }
    public required string EventId { get; set; }
    public required bool ReadOnly { get; set; }
    public required string EventType { get; set; }
    public required bool ManagementEvent { get; set; }
    public required string RecipientAccountId { get; set; }
    public ServiceEventDetails? ServiceEventDetails { get; set; }
}

public class ServiceEventDetails
{
    public CreateManagedAccountStatus? CreateManagedAccountStatus { get; set; }
}

public class CreateManagedAccountStatus
{
    public OrganizationalUnit OrganizationalUnit { get; set; }
    public AccountInfo Account { get; set; }
    public required string State { get; set; }
    public required string Message { get; set; }
}

public struct AccountInfo
{
    public required string AccountName { get; set; }
    public required string AccountId { get; set; }
}

public struct OrganizationalUnit
{
    public string? OrganizationalUnitName { get; set; }
    public string? OrganizationalUnitId { get; set; }
}