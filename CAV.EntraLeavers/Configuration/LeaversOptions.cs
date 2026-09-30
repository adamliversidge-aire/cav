namespace CAV.EntraLeavers.Configuration;

/// <summary>
/// Runtime settings, read from the Lambda environment variables set in Infrastructure/lambda.tf.
/// </summary>
public sealed record LeaversOptions
{
    public const int DefaultInactiveDays = 90;
    public const int DefaultMaxDisablePerRun = 25;
    public const string DefaultEmailTagKey = "email";

    public int InactiveDays { get; init; } = DefaultInactiveDays;

    /// <summary>When true, report what would be disabled without changing IAM.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Live runs abort (and alert) if more users than this would be disabled.</summary>
    public int MaxDisablePerRun { get; init; } = DefaultMaxDisablePerRun;

    public required string SnsTopicArn { get; init; }
    public required string EntraSecretArn { get; init; }
    public string IamEmailTagKey { get; init; } = DefaultEmailTagKey;

    public static LeaversOptions FromEnvironment() => FromEnvironment(Environment.GetEnvironmentVariable);

    internal static LeaversOptions FromEnvironment(Func<string, string?> getEnv) => new()
    {
        InactiveDays = ParsePositiveInt(getEnv("INACTIVE_DAYS"), "INACTIVE_DAYS", DefaultInactiveDays),
        // Anything other than an explicit "false" keeps dry-run on, so a typo can't make the run destructive.
        DryRun = !string.Equals(getEnv("DRY_RUN")?.Trim(), "false", StringComparison.OrdinalIgnoreCase),
        MaxDisablePerRun = ParsePositiveInt(getEnv("MAX_DISABLE_PER_RUN"), "MAX_DISABLE_PER_RUN", DefaultMaxDisablePerRun),
        SnsTopicArn = Required(getEnv("SNS_TOPIC_ARN"), "SNS_TOPIC_ARN"),
        EntraSecretArn = Required(getEnv("ENTRA_SECRET_ARN"), "ENTRA_SECRET_ARN"),
        IamEmailTagKey = string.IsNullOrWhiteSpace(getEnv("IAM_EMAIL_TAG_KEY"))
            ? DefaultEmailTagKey
            : getEnv("IAM_EMAIL_TAG_KEY")!.Trim()
    };

    private static int ParsePositiveInt(string? value, string name, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 1)
        {
            throw new InvalidOperationException($"{name} must be a positive whole number, got '{value}'.");
        }

        return parsed;
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Environment variable {name} is required.")
            : value.Trim();
}
