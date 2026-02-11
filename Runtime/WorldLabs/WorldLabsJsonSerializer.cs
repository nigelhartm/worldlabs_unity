using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WorldLabs.API
{
    /// <summary>
    /// Custom JSON serializer that handles the WorldLabs API's polymorphic types
    /// and null value handling better than Unity's built-in JsonUtility.
    /// </summary>
    public static class WorldLabsJsonSerializer
    {
        /// <summary>
        /// Serializes an object to JSON.
        /// </summary>
        public static string Serialize(object obj)
        {
            if (obj == null) return "null";

            var sb = new StringBuilder();
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        private static void SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            Type type = value.GetType();

            if (type == typeof(string))
            {
                SerializeString((string)value, sb);
            }
            else if (type == typeof(bool))
            {
                sb.Append((bool)value ? "true" : "false");
            }
            else if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            {
                sb.Append(value.ToString());
            }
            else if (type == typeof(float))
            {
                sb.Append(((float)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (type == typeof(double))
            {
                sb.Append(((double)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (type.IsEnum)
            {
                SerializeString(value.ToString(), sb);
            }
            else if (value is IList list)
            {
                SerializeList(list, sb);
            }
            else if (value is IDictionary dict)
            {
                SerializeDictionary(dict, sb);
            }
            else if (type.IsClass || type.IsValueType)
            {
                SerializeObject(value, sb);
            }
            else
            {
                sb.Append("null");
            }
        }

        private static void SerializeString(string value, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SerializeList(IList list, StringBuilder sb)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeValue(item, sb);
            }
            sb.Append(']');
        }

        private static void SerializeDictionary(IDictionary dict, StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeString(entry.Key.ToString(), sb);
                sb.Append(':');
                SerializeValue(entry.Value, sb);
            }
            sb.Append('}');
        }

        private static void SerializeObject(object obj, StringBuilder sb)
        {
            Type type = obj.GetType();

            // Handle nullable types
            if (Nullable.GetUnderlyingType(type) != null)
            {
                SerializeValue(obj, sb);
                return;
            }

            sb.Append('{');
            bool first = true;

            // Get all fields (public and private with SerializeField)
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            foreach (var field in fields)
            {
                // Skip if NonSerialized
                if (field.GetCustomAttribute<NonSerializedAttribute>() != null)
                    continue;

                // For private fields, only include if they have SerializeField
                if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null)
                    continue;

                object value = field.GetValue(obj);

                // Skip null values
                if (value == null) continue;

                // Handle nullable types
                if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    // Value is not null, so we can serialize it
                }

                // Skip empty lists
                if (value is IList list && list.Count == 0)
                    continue;

                if (!first) sb.Append(',');
                first = false;

                // Handle the "public" field name which is a C# keyword
                string fieldName = field.Name;
                if (fieldName == "public")
                {
                    fieldName = "public";
                }

                SerializeString(fieldName, sb);
                sb.Append(':');
                SerializeValue(value, sb);
            }

            // Also check for properties with getters (for polymorphic "type" property)
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead) continue;
                if (prop.GetIndexParameters().Length > 0) continue;

                // Only serialize "type" property for WorldPrompt subclasses
                if (prop.Name != "type") continue;

                try
                {
                    object value = prop.GetValue(obj);
                    if (value == null) continue;

                    if (!first) sb.Append(',');
                    first = false;

                    SerializeString(prop.Name, sb);
                    sb.Append(':');
                    SerializeValue(value, sb);
                }
                catch { }
            }

            sb.Append('}');
        }
    }

    /// <summary>
    /// Simple JSON parser for handling Dictionary fields that Unity's JsonUtility can't deserialize.
    /// </summary>
    public static class WorldLabsJsonParser
    {
        /// <summary>
        /// Parses a JSON string into a Dictionary of string to string.
        /// Used for spz_urls and similar structures.
        /// </summary>
        public static Dictionary<string, string> ParseStringDictionary(string json)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return result;

            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) return result;

            // Remove outer braces
            json = json.Substring(1, json.Length - 2).Trim();
            if (string.IsNullOrEmpty(json)) return result;

            int pos = 0;
            while (pos < json.Length)
            {
                // Skip whitespace
                while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
                if (pos >= json.Length) break;

                // Parse key
                string key = ParseString(json, ref pos);
                if (key == null) break;

                // Skip whitespace and colon
                while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ':')) pos++;
                if (pos >= json.Length) break;

                // Parse value
                string value = ParseString(json, ref pos);
                if (value != null)
                {
                    result[key] = value;
                }

                // Skip whitespace and comma
                while (pos < json.Length && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;
            }

            return result;
        }

        private static string ParseString(string json, ref int pos)
        {
            // Skip whitespace
            while (pos < json.Length && char.IsWhiteSpace(json[pos])) pos++;
            if (pos >= json.Length || json[pos] != '"') return null;

            pos++; // Skip opening quote
            var sb = new StringBuilder();

            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '"')
                {
                    pos++; // Skip closing quote
                    return sb.ToString();
                }
                else if (c == '\\' && pos + 1 < json.Length)
                {
                    pos++;
                    char escaped = json[pos];
                    switch (escaped)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(escaped); break;
                    }
                    pos++;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Extracts a JSON object substring for a given property name.
        /// </summary>
        public static string ExtractJsonObject(string json, string propertyName)
        {
            string searchPattern = $"\"{propertyName}\"";
            int propIndex = json.IndexOf(searchPattern);
            if (propIndex == -1) return null;

            // Find the colon after the property name
            int colonIndex = json.IndexOf(':', propIndex + searchPattern.Length);
            if (colonIndex == -1) return null;

            // Find the opening brace
            int startIndex = json.IndexOf('{', colonIndex);
            if (startIndex == -1) return null;

            // Find matching closing brace
            int braceCount = 1;
            int endIndex = startIndex + 1;
            while (endIndex < json.Length && braceCount > 0)
            {
                if (json[endIndex] == '{') braceCount++;
                else if (json[endIndex] == '}') braceCount--;
                endIndex++;
            }

            if (braceCount == 0)
            {
                return json.Substring(startIndex, endIndex - startIndex);
            }

            return null;
        }

        /// <summary>
        /// Post-processes a World object to parse the spz_urls dictionary.
        /// Call this after using JsonUtility.FromJson on the API response.
        /// </summary>
        public static void PostProcessWorld(World world, string rawJson)
        {
            if (world?.assets?.splats == null) return;

            try
            {
                // Extract the spz_urls object from the raw JSON
                string splatsJson = ExtractJsonObject(rawJson, "splats");
                if (string.IsNullOrEmpty(splatsJson)) return;

                string spzUrlsJson = ExtractJsonObject(splatsJson, "spz_urls");
                if (string.IsNullOrEmpty(spzUrlsJson)) return;

                world.assets.splats.spz_urls = ParseStringDictionary(spzUrlsJson);
                world.assets.splats.spz_urls_raw = spzUrlsJson;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse spz_urls: {ex.Message}");
            }
        }

        /// <summary>
        /// Post-processes a GetOperationResponse to parse nested World's spz_urls.
        /// </summary>
        public static void PostProcessOperationResponse(GetOperationResponse response, string rawJson)
        {
            if (response?.response == null) return;

            try
            {
                // Extract the response object
                string responseJson = ExtractJsonObject(rawJson, "response");
                if (!string.IsNullOrEmpty(responseJson))
                {
                    PostProcessWorld(response.response, responseJson);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to post-process operation response: {ex.Message}");
            }
        }

        /// <summary>
        /// Post-processes a ListWorldsResponse to parse all World's spz_urls.
        /// </summary>
        public static void PostProcessListWorldsResponse(ListWorldsResponse response, string rawJson)
        {
            if (response?.worlds == null) return;

            try
            {
                // Find the worlds array and process each world
                int worldsStart = rawJson.IndexOf("\"worlds\"");
                if (worldsStart == -1) return;

                int arrayStart = rawJson.IndexOf('[', worldsStart);
                if (arrayStart == -1) return;

                // For each world, extract and process
                int currentPos = arrayStart + 1;
                int worldIndex = 0;

                while (currentPos < rawJson.Length && worldIndex < response.worlds.Count)
                {
                    // Skip whitespace
                    while (currentPos < rawJson.Length && char.IsWhiteSpace(rawJson[currentPos])) currentPos++;

                    if (rawJson[currentPos] == ']') break;
                    if (rawJson[currentPos] == ',') { currentPos++; continue; }

                    if (rawJson[currentPos] == '{')
                    {
                        // Find matching closing brace
                        int braceCount = 1;
                        int worldStart = currentPos;
                        currentPos++;

                        while (currentPos < rawJson.Length && braceCount > 0)
                        {
                            if (rawJson[currentPos] == '{') braceCount++;
                            else if (rawJson[currentPos] == '}') braceCount--;
                            currentPos++;
                        }

                        if (braceCount == 0)
                        {
                            string worldJson = rawJson.Substring(worldStart, currentPos - worldStart);
                            PostProcessWorld(response.worlds[worldIndex], worldJson);
                            worldIndex++;
                        }
                    }
                    else
                    {
                        currentPos++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to post-process list worlds response: {ex.Message}");
            }
        }
    }
}
