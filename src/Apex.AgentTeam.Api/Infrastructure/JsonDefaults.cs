using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apex.AgentTeam.Api.Infrastructure;

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    static JsonDefaults()
    {
        Web.Converters.Add(new JsonStringEnumConverter());
    }
}
