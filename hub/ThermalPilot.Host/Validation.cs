using ThermalPilot.Contracts;

namespace ThermalPilot.Host;

public static class Validation
{
    public static void Profile(CreateProfileRequest request)
    {
        Text(request.Name, 80, "profile_name");
        if (!Enum.IsDefined(request.BaseMode))
            throw new RequestFailure("invalid_mode", "Select a supported base mode.", 400);
    }

    private static void Text(string? text, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maximumLength || text.Any(char.IsControl))
            throw new RequestFailure("invalid_" + field, $"Provide {field} using 1-{maximumLength} characters without control characters.", 400);
    }

}
