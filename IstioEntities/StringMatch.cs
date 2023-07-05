using System.Text.Json.Serialization;

namespace RouteControlService.IstioEntities;

public enum StringMatchType
{
    Exact,
    Prefix,
    Regex
}

[Serializable]
public class StringMatch : Dictionary<string, string>
{
    [JsonIgnore]
    public StringMatchType Type
    {
        get => this.FirstOrDefault().Key switch
        {
            "exact" => StringMatchType.Exact,
            "prefix" => StringMatchType.Prefix,
            _ => StringMatchType.Regex
        };
        set
        {
            var val = this.FirstOrDefault().Value;
            Clear();
            Add(Enum.GetName(value)?.ToLower() ?? "exact", val);
        }
    }

    [JsonIgnore]
    public string Value
    {
        get => this.FirstOrDefault().Value;
        set
        {
            var val = this.FirstOrDefault().Key;
            Clear();
            Add(val, value);
        }
    }
}