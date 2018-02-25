using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConsulRx.Configuration
{
    public sealed class JsonNodeParser
    {
        public IEnumerable<KeyValuePair<string, string>> Parse(string stream)
        {
            using (var streamReader = new StringReader(stream))
            using (var jsonReader = new JsonTextReader(streamReader))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                JObject jsonConfig = JObject.Load(jsonReader);
                return Traverse(jsonConfig);
            }
        }

        internal static IEnumerable<KeyValuePair<string, string>> Traverse(JObject jObject)
        {
            var visitor = new JsonVisitor();
            IDictionary<string, string> data = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> primitive in visitor.VisitJObject(jObject))
            {
                if (data.ContainsKey(primitive.Key))
                {
                    throw new FormatException($"Key {primitive.Key} is duplicated in json");
                }

                data.Add(primitive);
                yield return primitive;
            }

            //return data;
        }

        private sealed class JsonVisitor
        {
            private readonly Stack<string> _context = new Stack<string>();

            internal IEnumerable<KeyValuePair<string, string>> VisitJObject(JObject jObject)
            {
                return jObject.Properties().SelectMany(property => VisitProperty(property.Name, property.Value));
            }

            private IEnumerable<KeyValuePair<string, string>> VisitArray(JArray array)
            {
                return array.SelectMany((token, index) => VisitProperty(index.ToString(), token));
            }

            private IEnumerable<KeyValuePair<string, string>> VisitPrimitive(JToken primitive)
            {
                if (_context.Count == 0)
                {
                    throw new FormatException("Error visiting primitive, the context was empty.");
                }

                string key = ConfigurationPath.Combine(_context.Reverse());
                return new[]
                {
                    new KeyValuePair<string, string>(key, primitive.ToString())
                };
            }

            private IEnumerable<KeyValuePair<string, string>> VisitProperty(string key, JToken token)
            {
                _context.Push(key);
                foreach (var keyValuePair in VisitToken(token))
                {
                    yield return keyValuePair;
                }
                _context.Pop();
            }

            private IEnumerable<KeyValuePair<string, string>> VisitToken(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return VisitJObject(token.Value<JObject>());
                    case JTokenType.Array:
                        return VisitArray(token.Value<JArray>());
                    case JTokenType.Integer:
                    case JTokenType.Float:
                    case JTokenType.String:
                    case JTokenType.Boolean:
                    case JTokenType.Bytes:
                    case JTokenType.Raw:
                    case JTokenType.Null:
                        return VisitPrimitive(token);
                    default:
                        throw new FormatException($"Error parsing JSON. {token.Type} is not a supported token.");
                }
            }
        }
    }
}