using System.Text;
using System.Text.RegularExpressions;
using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public static partial class Validation
{
    public static void Profile(CreateProfileRequest request)
    {
        Text(request.Name, 80, "profile_name");
        if (!Enum.IsDefined(request.BaseMode))
            throw new RequestFailure("invalid_mode", "Select a supported base mode.", 400);
    }

    public static void Command(SimulateCommandRequest request)
    {
        if (request.ProfileId == Guid.Empty || request.CorrelationId == Guid.Empty || request.ExpectedProfileVersion < 1)
            throw new RequestFailure("invalid_command", "A profile, version and correlation ID are required.", 400);
        if (request.IdempotencyKey is null || !IdempotencyPattern().IsMatch(request.IdempotencyKey))
            throw new RequestFailure("invalid_idempotency_key", "Use an 8-100 character idempotency key containing letters, digits, underscores or hyphens.", 400);
    }

    public static void Report(CreateSyntheticReportRequest request)
    {
        Text(request.Title, 160, "report_title");
        if (string.IsNullOrWhiteSpace(request.Body) || Encoding.UTF8.GetByteCount(request.Body) > 16_000 ||
            request.Body.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t'))
            throw new RequestFailure("invalid_report_body", "Provide a report body of at most 16000 UTF-8 bytes.", 400);
    }

    private static void Text(string? text, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Any(char.IsControl))
            throw new RequestFailure("invalid_" + field, $"Provide {field} using 1-{maximumLength} characters without control characters.", 400);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyPattern();
}
