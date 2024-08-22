using HandheldCompanion.Managers;
using HandheldCompanion.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;

namespace HandheldCompanion.Utils;

public static class EnumUtils
{
    public static T GetAttributeOfType<T>(this Type type, string value) where T : Attribute
    {
        var attr = type
            .GetField(value)
            .GetCustomAttributes(typeof(T), false)
            .FirstOrDefault();
        return attr != null ? (T)attr : null;
    }
    // This extension method is broken out so you can use a similar pattern with 
    // other MetaData elements in the future. This is your base method for each.
    //
    public static T GetAttribute<T>(this Enum value) where T : Attribute
    {
        //var type = value.GetType();
        //var memberInfo = type.GetMember(value.ToString());
        //var attributes = memberInfo[0].GetCustomAttributes(typeof(T), false);
        //return (T)attributes[0];
        var attr = value
            .GetType()
            .GetField(value.ToString())
            .GetCustomAttributes(typeof(T), false)
            .FirstOrDefault();
        return attr != null ? (T)attr : null;
    }

    public static string GetDescriptionFromEnumValue(Enum value, string prefix = "", string suffix = "")
    {
        // return localized string if available
        string key;

        if (!string.IsNullOrEmpty(prefix))
            key = $"Enum_{prefix}_{value.GetType().Name}_{value}";
        else if (!string.IsNullOrEmpty(suffix))
            key = $"Enum_{value.GetType().Name}_{value}_{suffix}";
        else
            key = $"Enum_{value.GetType().Name}_{value}";

        var root = Resources.ResourceManager.GetString(key);

        if (root is not null)
            return root;

        // return description otherwise
        DescriptionAttribute attribute = null;

        try
        {
            attribute = value.GetType()
                        .GetField(value.ToString())
                        .GetCustomAttributes(typeof(DescriptionAttribute), false)
                        .SingleOrDefault() as DescriptionAttribute;
        }
        catch { }

        if (attribute is not null)
            return attribute.Description;

        LogManager.LogError("Neither localization nor description exists for enum: {0}", key);
        return value.ToString();
    }

    public static T GetEnumValueFromDescription<T>(string description)
    {
        var type = typeof(T);
        if (!type.IsEnum)
            throw new ArgumentException();
        var fields = type.GetFields();
        var field = fields
            .SelectMany(f => f.GetCustomAttributes(typeof(DescriptionAttribute), false), (f, a) => new { Field = f, Att = a })
            .SingleOrDefault(a => ((DescriptionAttribute)a.Att).Description == description);
        return field is null ? default : (T)field.Field.GetRawConstantValue();
    }
}

public static class EnumUtils<T> where T : struct
{

    static readonly IEnumerable<T> all = Enum.GetValues(typeof(T)).OfType<T>().Distinct();
    static readonly Dictionary<string, T> insensitiveNames = all.ToDictionary(k => Enum.GetName(typeof(T), k).ToUpperInvariant());
    static readonly Dictionary<string, T> sensitiveNames = all.ToDictionary(k => Enum.GetName(typeof(T), k));
    static readonly Dictionary<int, T> values = all.ToDictionary(k => Convert.ToInt32(k));
    static readonly Dictionary<T, string> names = all.ToDictionary(
        k => k,
        v => v.ToString()
    );
    static readonly Dictionary<T, string> localizedNames = all.ToDictionary(
        k => k,
        v =>
        {
            var attr = typeof(T).GetAttributeOfType<DescriptionAttribute>(v.ToString());
            if (attr != null)
                return attr.Description;
            return v.ToString();
        });

    public static bool IsDefined(T value) => names.Keys.Contains(value);

    public static bool IsDefined(string v) => string.IsNullOrEmpty(v)
        ? false
        : sensitiveNames.Keys.Contains(v);

    public static bool IsDefined(int? v) => v != null && values.Keys.Contains(v.Value);

    public static IEnumerable<T> GetValues() => all;

    public static string[] GetNames() => names.Values.ToArray();

    public static string GetName(T value)
    {
        names.TryGetValue(value, out string name);
        return name;
    }

    public static string GetLocalizedName(T value)
    {
        localizedNames.TryGetValue(value, out string name);
        return name;
    }

    public static T Parse(int value)
    {
        if (!values.TryGetValue(value, out T parsed))
            throw new ArgumentException("Value is not one of the named constants defined for the enumeration", "value");
        return parsed;
    }

    public static T Parse(string value)
    {
        if (!sensitiveNames.TryGetValue(value, out T parsed))
            throw new ArgumentException("Value is not one of the named constants defined for the enumeration", "value");
        return parsed;
    }

    public static T Parse(string value, bool ignoreCase)
    {
        if (!ignoreCase)
            return Parse(value);

        if (!insensitiveNames.TryGetValue(value.ToUpperInvariant(), out T parsed))
            throw new ArgumentException("Value is not one of the named constants defined for the enumeration", "value");
        return parsed;
    }

    public static bool TryParse(int? value, out T returnValue)
    {
        returnValue = default;
        return value == null ? false : values.TryGetValue(value.Value, out returnValue);
    }

    public static bool TryParse(string value, out T returnValue)
    {
        if (value == null)
        {
            returnValue = default;
            return false;
        }

        return sensitiveNames.TryGetValue(value, out returnValue);
    }

    public static bool TryParse(string value, bool ignoreCase, out T returnValue) => ignoreCase
                   ? insensitiveNames.TryGetValue(value.ToUpperInvariant(), out returnValue)
                   : TryParse(value, out returnValue);

    public static T? ParseOrNull(int? value) => !IsDefined(value)
        ? null
        : values.TryGetValue(value.Value, out T foundValue) ? foundValue : null;

    public static T? ParseOrNull(string name) => string.IsNullOrEmpty(name)
        ? null
        : sensitiveNames.TryGetValue(name, out T foundValue) ? foundValue : null;

    public static T? ParseOrNull(string value, bool ignoreCase)
    {
        if (!ignoreCase)
            return ParseOrNull(value);

        if (string.IsNullOrEmpty(value))
            return null;

        return insensitiveNames.TryGetValue(value.ToUpperInvariant(), out T foundValue) ? foundValue : null;
    }

    public static T? CastOrNull(int value) => values.TryGetValue(value, out T foundValue) ? foundValue : null;

    public static IEnumerable<T> GetFlags(T flagEnum)
    {
        var flagInt = Convert.ToInt32(flagEnum);
        return all.Where(e => (Convert.ToInt32(e) & flagInt) != 0);
    }

    public static T SetFlags(IEnumerable<T> flags)
    {
        var combined = flags.Aggregate(default(int), (current, flag) => current | Convert.ToInt32(flag));
        return values.TryGetValue(combined, out T result) ? result : default;
    }

}