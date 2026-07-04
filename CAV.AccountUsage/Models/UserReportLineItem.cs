using System.Text.Json.Serialization;

namespace CAV.AccountUsage.Models;

public class UserReportLineItem
{
    [CsvHelper.Configuration.Attributes.Name("user")]
    public string? User { get; set; }

    [CsvHelper.Configuration.Attributes.Name("arn")]
    public string? Arn { get; set; }

    [CsvHelper.Configuration.Attributes.Name("user_creation_time")]
    public DateTime? UserCreationTime { get; set; }

    /// <summary>"true", "false", or "not_supported"</summary>
    [CsvHelper.Configuration.Attributes.Name("password_enabled")]
    public string? PasswordEnabled { get; set; }

    /// <summary>"N/A", "no_information", or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("password_last_used")]
    public string? PasswordLastUsed { get; set; }

    /// <summary>"not_supported" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("password_last_changed")]
    public string? PasswordLastChanged { get; set; }

    /// <summary>"not_supported" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("password_next_rotation")]
    public string? PasswordNextRotation { get; set; }

    [CsvHelper.Configuration.Attributes.Name("mfa_active")]
    public bool MfaActive { get; set; }

    // --- Access Key 1 ---

    [CsvHelper.Configuration.Attributes.Name("access_key_1_active")]
    public bool AccessKey1Active { get; set; }

    /// <summary>"N/A" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("access_key_1_last_rotated")]
    public string? AccessKey1LastRotated { get; set; }

    /// <summary>"N/A" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("access_key_1_last_used_date")]
    public string? AccessKey1LastUsedDate { get; set; }

    [CsvHelper.Configuration.Attributes.Name("access_key_1_last_used_region")]
    public string? AccessKey1LastUsedRegion { get; set; }

    [CsvHelper.Configuration.Attributes.Name("access_key_1_last_used_service")]
    public string? AccessKey1LastUsedService { get; set; }

    // --- Access Key 2 ---

    [CsvHelper.Configuration.Attributes.Name("access_key_2_active")]
    public bool AccessKey2Active { get; set; }

    /// <summary>"N/A" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("access_key_2_last_rotated")]
    public string? AccessKey2LastRotated { get; set; }

    /// <summary>"N/A" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("access_key_2_last_used_date")]
    public string? AccessKey2LastUsedDate { get; set; }

    [CsvHelper.Configuration.Attributes.Name("access_key_2_last_used_region")]
    public string? AccessKey2LastUsedRegion { get; set; }

    [CsvHelper.Configuration.Attributes.Name("access_key_2_last_used_service")]
    public string? AccessKey2LastUsedService { get; set; }

    // --- Certificates ---

    [CsvHelper.Configuration.Attributes.Name("cert_1_active")]
    public bool Cert1Active { get; set; }

    /// <summary>"N/A" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("cert_1_last_rotated")]
    public string? Cert1LastRotated { get; set; }

    [CsvHelper.Configuration.Attributes.Name("cert_2_active")]
    public bool Cert2Active { get; set; }

    /// <summary>"N/A" or a date</summary>
    [CsvHelper.Configuration.Attributes.Name("cert_2_last_rotated")]
    public string? Cert2LastRotated { get; set; }
}
