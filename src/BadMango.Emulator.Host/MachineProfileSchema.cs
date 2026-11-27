// <copyright file="MachineProfileSchema.cs" company="Bad Mango Solutions">
// Copyright (c) Bad Mango Solutions. All rights reserved.
// </copyright>

namespace BadMango.Emulator.Host;

using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Checks a machine profile document against the embedded <c>schemas/machine-profile.schema.json</c>.
/// </summary>
internal static class MachineProfileSchema
{
    private const string ResourceName = "BadMango.Emulator.Host.Schemas.machine-profile.schema.json";
    private const int MaxErrors = 8;

    private static readonly FrozenSet<string> Annotations = new string[]
    {
        "$schema",
        "$id",
        "title",
        "description",
        "$comment",
        "default",
        "examples",
        "$defs",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ImplementedKeywords = new string[]
    {
        "$ref",
        "type",
        "enum",
        "pattern",
        "minLength",
        "minimum",
        "maximum",
        "minItems",
        "items",
        "properties",
        "required",
        "propertyNames",
        "additionalProperties",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly Lazy<JsonObject> Schema = new(Load);

    /// <summary>
    /// Loads the embedded schema and rejects it when a keyword is not implemented.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the embedded schema uses an unsupported keyword.</exception>
    internal static void EnsureEmbeddedSchema()
    {
        _ = Schema.Value;
    }

    /// <summary>
    /// Rejects <paramref name="schema"/> when it uses a keyword this checker does not implement.
    /// </summary>
    /// <param name="schema">A JSON Schema object.</param>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="schema"/> uses an unsupported keyword.</exception>
    internal static void EnsureSupported(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        CheckSchema(schema, "$");
    }

    /// <summary>
    /// Throws when <paramref name="json"/> does not match the machine profile schema.
    /// </summary>
    /// <param name="json">The merged profile document.</param>
    /// <exception cref="InvalidOperationException">Thrown when the document is not JSON or fails the schema.</exception>
    internal static void Validate(string json)
    {
        JsonNode? document;
        try
        {
            document = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The machine profile does not match schemas/machine-profile.schema.json: the document is not JSON.",
                ex);
        }

        List<string> errors = [];
        Evaluate(Schema.Value, Schema.Value, document, "$", errors);
        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "The machine profile does not match schemas/machine-profile.schema.json: " + string.Join("; ", errors) + ".");
    }

    private static JsonObject Load()
    {
        Assembly assembly = typeof(MachineProfileSchema).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                "schemas/machine-profile.schema.json is not embedded in the host assembly.");
        }

        JsonNode? node = JsonNode.Parse(stream);
        if (node is not JsonObject schema)
        {
            throw new InvalidOperationException(
                "schemas/machine-profile.schema.json is not a JSON object.");
        }

        CheckSchema(schema, "$");
        return schema;
    }

    private static void CheckSchema(JsonObject schema, string path)
    {
        foreach (KeyValuePair<string, JsonNode?> property in schema)
        {
            if (Annotations.Contains(property.Key))
            {
                if (property.Key == "$defs" && property.Value is JsonObject definitions)
                {
                    foreach (KeyValuePair<string, JsonNode?> definition in definitions)
                    {
                        if (definition.Value is JsonObject definitionSchema)
                        {
                            CheckSchema(definitionSchema, path + ".$defs." + definition.Key);
                        }
                    }
                }

                continue;
            }

            if (!ImplementedKeywords.Contains(property.Key))
            {
                throw new InvalidOperationException(
                    $"schemas/machine-profile.schema.json uses unsupported keyword '{property.Key}' at {path}.{property.Key}.");
            }

            switch (property.Key)
            {
                case "properties" when property.Value is JsonObject properties:
                    foreach (KeyValuePair<string, JsonNode?> child in properties)
                    {
                        if (child.Value is JsonObject childSchema)
                        {
                            CheckSchema(childSchema, path + ".properties." + child.Key);
                        }
                    }

                    break;
                case "items" when property.Value is JsonObject itemSchema:
                    CheckSchema(itemSchema, path + ".items");
                    break;
                case "additionalProperties" when property.Value is JsonObject additionalSchema:
                    CheckSchema(additionalSchema, path + ".additionalProperties");
                    break;
                case "propertyNames" when property.Value is JsonObject nameSchema:
                    CheckSchema(nameSchema, path + ".propertyNames");
                    break;
            }
        }
    }

    private static void Evaluate(JsonObject root, JsonObject schema, JsonNode? instance, string path, List<string> errors)
    {
        if (errors.Count >= MaxErrors)
        {
            return;
        }

        if (schema.TryGetPropertyValue("$ref", out JsonNode? reference) && reference is JsonValue referenceValue
            && referenceValue.TryGetValue<string>(out string? pointer) && pointer is not null)
        {
            Evaluate(root, Resolve(root, pointer), instance, path, errors);
        }

        if (!TypeMatches(schema, instance))
        {
            errors.Add($"{path} does not match the schema type");
            return;
        }

        if (schema.TryGetPropertyValue("enum", out JsonNode? choices) && choices is JsonArray allowed && instance is not null)
        {
            bool found = false;
            foreach (JsonNode? choice in allowed)
            {
                if (choice is not null && Same(instance, choice))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                errors.Add($"{path} is not one of the allowed values");
            }
        }

        if (instance is JsonValue textValue && textValue.TryGetValue<string>(out string? text) && text is not null)
        {
            if (schema.TryGetPropertyValue("pattern", out JsonNode? patternNode)
                && patternNode is JsonValue patternValue
                && patternValue.TryGetValue<string>(out string? pattern)
                && pattern is not null
                && !Matches(text, pattern))
            {
                errors.Add($"{path} does not match {pattern}");
            }

            if (schema.TryGetPropertyValue("minLength", out JsonNode? minLengthNode)
                && minLengthNode is JsonValue minLengthValue
                && minLengthValue.TryGetValue<int>(out int minLength)
                && text.Length < minLength)
            {
                errors.Add($"{path} is shorter than {minLength.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (instance is JsonValue number && TryDecimal(number, out decimal numeric))
        {
            if (schema.TryGetPropertyValue("minimum", out JsonNode? minimumNode)
                && minimumNode is JsonValue minimumValue
                && TryDecimal(minimumValue, out decimal minimum)
                && numeric < minimum)
            {
                errors.Add($"{path} is below {minimum.ToString(CultureInfo.InvariantCulture)}");
            }

            if (schema.TryGetPropertyValue("maximum", out JsonNode? maximumNode)
                && maximumNode is JsonValue maximumValue
                && TryDecimal(maximumValue, out decimal maximum)
                && numeric > maximum)
            {
                errors.Add($"{path} is above {maximum.ToString(CultureInfo.InvariantCulture)}");
            }
        }

        if (instance is JsonArray array)
        {
            if (schema.TryGetPropertyValue("minItems", out JsonNode? minItemsNode)
                && minItemsNode is JsonValue minItemsValue
                && minItemsValue.TryGetValue<int>(out int minItems)
                && array.Count < minItems)
            {
                errors.Add($"{path} has fewer than {minItems.ToString(CultureInfo.InvariantCulture)} items");
            }

            if (schema.TryGetPropertyValue("items", out JsonNode? items) && items is JsonObject itemSchema)
            {
                for (int index = 0; index < array.Count; index++)
                {
                    Evaluate(root, itemSchema, array[index], path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]", errors);
                }
            }
        }

        if (instance is not JsonObject document)
        {
            return;
        }

        JsonObject? properties = schema.TryGetPropertyValue("properties", out JsonNode? propertiesNode)
            ? propertiesNode as JsonObject
            : null;
        if (schema.TryGetPropertyValue("required", out JsonNode? requiredNode) && requiredNode is JsonArray required)
        {
            foreach (string name in required
                .OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out string? parsed) ? parsed : null)
                .Where(name => name is not null)
                .Select(name => name!))
            {
                if (!document.ContainsKey(name))
                {
                    errors.Add($"{Extend(path, name)} is required");
                }
            }
        }

        if (schema.TryGetPropertyValue("propertyNames", out JsonNode? propertyNames) && propertyNames is JsonObject nameSchema)
        {
            foreach (KeyValuePair<string, JsonNode?> property in document)
            {
                Evaluate(root, nameSchema, JsonValue.Create(property.Key), Extend(path, property.Key), errors);
            }
        }

        JsonNode? additional = null;
        bool hasAdditional = schema.TryGetPropertyValue("additionalProperties", out additional);
        foreach (KeyValuePair<string, JsonNode?> property in document)
        {
            if (properties is not null && properties.TryGetPropertyValue(property.Key, out JsonNode? child) && child is JsonObject childSchema)
            {
                Evaluate(root, childSchema, property.Value, Extend(path, property.Key), errors);
                continue;
            }

            if (additional is JsonObject additionalSchema)
            {
                Evaluate(root, additionalSchema, property.Value, Extend(path, property.Key), errors);
                continue;
            }

            if (hasAdditional && additional is JsonValue flag && flag.TryGetValue<bool>(out bool allow) && !allow)
            {
                errors.Add($"{Extend(path, property.Key)} is not a property of the schema");
            }
        }
    }

    private static bool TypeMatches(JsonObject schema, JsonNode? instance)
    {
        if (!schema.TryGetPropertyValue("type", out JsonNode? typeNode) || typeNode is null)
        {
            return true;
        }

        if (typeNode is JsonArray types)
        {
            foreach (JsonNode? type in types)
            {
                if (type is JsonValue typeValue && typeValue.TryGetValue<string>(out string? name) && KindMatches(name, instance))
                {
                    return true;
                }
            }

            return false;
        }

        if (typeNode is JsonValue single && single.TryGetValue<string>(out string? typeName))
        {
            return KindMatches(typeName, instance);
        }

        return true;
    }

    private static bool KindMatches(string typeName, JsonNode? instance)
    {
        JsonValueKind kind = instance?.GetValueKind() ?? JsonValueKind.Null;
        return typeName switch
        {
            "object" => kind == JsonValueKind.Object,
            "array" => kind == JsonValueKind.Array,
            "string" => kind == JsonValueKind.String,
            "boolean" => kind is JsonValueKind.True or JsonValueKind.False,
            "null" => kind == JsonValueKind.Null,
            "number" => kind == JsonValueKind.Number,
            "integer" => kind == JsonValueKind.Number && instance is JsonValue number && IsInteger(number),
            _ => false,
        };
    }

    private static bool IsInteger(JsonValue number)
    {
        if (number.TryGetValue<long>(out _))
        {
            return true;
        }

        if (number.TryGetValue<decimal>(out decimal exact))
        {
            return exact == decimal.Truncate(exact);
        }

        // Exact equality is the JSON Schema integer test. A tolerance would accept non-integers.
        return number.TryGetValue<double>(out double real) && double.IsFinite(real) && real == Math.Truncate(real);
    }

    private static bool TryDecimal(JsonValue number, out decimal value)
    {
        if (number.TryGetValue<decimal>(out value))
        {
            return true;
        }

        if (number.TryGetValue<long>(out long whole))
        {
            value = whole;
            return true;
        }

        if (number.TryGetValue<double>(out double real) && double.IsFinite(real))
        {
            value = (decimal)real;
            return true;
        }

        value = 0;
        return false;
    }

    private static bool Same(JsonNode left, JsonNode right)
    {
        if (left is JsonValue leftValue && right is JsonValue rightValue)
        {
            if (leftValue.TryGetValue<string>(out string? leftText) && rightValue.TryGetValue<string>(out string? rightText))
            {
                return string.Equals(leftText, rightText, StringComparison.Ordinal);
            }

            if (TryDecimal(leftValue, out decimal leftNumber) && TryDecimal(rightValue, out decimal rightNumber))
            {
                return leftNumber == rightNumber;
            }

            if (leftValue.TryGetValue<bool>(out bool leftFlag) && rightValue.TryGetValue<bool>(out bool rightFlag))
            {
                return leftFlag == rightFlag;
            }
        }

        return string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal);
    }

    private static bool Matches(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static JsonObject Resolve(JsonObject root, string pointer)
    {
        if (!pointer.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Schema pointer '{pointer}' is not local to schemas/machine-profile.schema.json.");
        }

        JsonNode current = root;
        foreach (string raw in pointer[2..].Split('/'))
        {
            string token = raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(token, out JsonNode? next) || next is null)
            {
                throw new InvalidOperationException($"Schema pointer '{pointer}' does not resolve.");
            }

            current = next;
        }

        if (current is not JsonObject schema)
        {
            throw new InvalidOperationException($"Schema pointer '{pointer}' is not an object.");
        }

        return schema;
    }

    private static string Extend(string path, string name)
    {
        if (name.Length > 0 && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            return path + "." + name;
        }

        return path + "['" + name + "']";
    }
}