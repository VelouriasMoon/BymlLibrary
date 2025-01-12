﻿using SharpYaml.Serialization;
using System.Globalization;
using System.Diagnostics;
using Byml.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;

namespace Nintendo.Byml.Parser
{
    public class YamlConverter
    {
        private static Dictionary<string, BymlNode> ReferenceNodes { get; set; } = new Dictionary<string, BymlNode>();
        private static bool ContainsEmbedded { get; set; } = false;
        private static Dictionary<BymlNode, byte[]> EmbeddedFiles { get; set; } = new Dictionary<BymlNode, byte[]>();

        public static string ToYaml(BymlFile byml)
        {
            YamlNode root = SaveNode(byml.RootNode);
            YamlStream stream = new(new YamlDocument(root));
            string ret;
            using (StringWriter writer = new(new StringBuilder()))
            {
                stream.Save(writer, true);
                ret = writer.ToString();
            }
            return ret;
        }

        public static string ToYaml(BymlNode byml)
        {
            YamlNode root = SaveNode(byml);
            YamlStream stream = new(new YamlDocument(root));
            string ret;
            using (StringWriter writer = new(new StringBuilder()))
            {
                stream.Save(writer, true);
                ret = writer.ToString();
            }
            return ret;
        }

        public static BymlFile FromYaml(string text)
        {
            ReferenceNodes.Clear();

            var byml = new BymlFile();
            var yaml = new YamlStream();

            yaml.Load(new StringReader(text));

            YamlNode root = yaml.Documents[0].RootNode;

            if (root is YamlMappingNode || root is YamlSequenceNode)
                byml.RootNode = ParseNode(root);

            ReferenceNodes.Clear();

            if (ContainsEmbedded)
            {
                byml.SetEmbedded(EmbeddedFiles);
            }

            return byml;
        }

        static BymlNode ParseNode(YamlNode node)
        {
            if (node is YamlMappingNode castMappingNode) {
                var values = new Dictionary<string, BymlNode>();
                if (IsValidReference(node))
                    ReferenceNodes.Add(node.Tag, new BymlNode(values));

                foreach (var child in castMappingNode.Children) {
                    var key = ((YamlScalarNode)child.Key).Value;
                    var tag = ((YamlScalarNode)child.Key).Tag;
                    if (tag == "!h")
                        key = Crc32.Compute(key).ToString("x");

                    values.Add(key, ParseNode(child.Value));
                }
                return new BymlNode(values);
            }
            else if (node is YamlSequenceNode castSequenceNode) {

                var values = new List<BymlNode>();
                if (IsValidReference(node))
                    ReferenceNodes.Add(node.Tag, new BymlNode(values));

                foreach (var child in castSequenceNode.Children)
                    values.Add(ParseNode(child));

                return new BymlNode(values);
            }
            else if (node is YamlScalarNode castScalarNode && castScalarNode.Value.Contains("!refTag=")) {

                string tag = castScalarNode.Value.Replace("!refTag=", string.Empty);
                Debug.WriteLine($"refNode {tag} {ReferenceNodes.ContainsKey(tag)}");

                if (ReferenceNodes.ContainsKey(tag))
                    return ReferenceNodes[tag];
                else {
                    Console.WriteLine("Failed to find reference node! " + tag);
                    return null;
                }
            }
            else {
                return ConvertValue(((YamlScalarNode)node).Value, ((YamlScalarNode)node).Tag);
            }
        }

        static bool IsValidReference(YamlNode node) => node.Tag != null && node.Tag.Contains("!ref") && !ReferenceNodes.ContainsKey(node.Tag);

        static BymlNode ConvertValue(string value, string tag)
        {
            if (tag == null)
            {
                tag = "";
            }

            if (value == "null")
            {
                return new BymlNode();
            }
            else if (value is "true" or "True")
            {
                return new BymlNode(true);
            }
            else if (value is "false" or "False")
            {
                return new BymlNode(false);
            }
            else if (tag == "!u")
            {
                return new BymlNode(Convert.ToUInt32(value, 16));
            }
            else if (tag == "!d")
            {
                if (double.TryParse(value, out double outvalue))
                    return new BymlNode(outvalue);
            }
            else if (tag == "!ul")
            {
                if (ulong.TryParse(value, out ulong outvalue))
                    return new BymlNode(outvalue);
            }
            else if (tag == "!l")
            {
                if (long.TryParse(value, out long outvalue))
                    return new BymlNode(outvalue);
            }
            else if (tag == "!h")
            {
                return new BymlNode(Crc32.Compute(value).ToString("x"));
            }
            else if (tag == "!e")
            {
                ContainsEmbedded = true;
                byte[] data = Convert.FromBase64String(value);
                BymlNode newnode = new BymlNode(data, true);
                EmbeddedFiles.Add(newnode, data);
                return newnode;
            }
            else
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
                {
                    return new BymlNode(intValue);
                }
                if (float.TryParse(value, out float floatValue))
                {
                    return new BymlNode(floatValue);
                }
            }

            return new BymlNode(value != "''" ? value : string.Empty);
        }

        static YamlNode SaveNode(BymlNode node)
        {
            if (node == null)
            {
                return new YamlScalarNode("null");
            }
            else if (node.Type == NodeType.Array)
            {
                var yamlNode = new YamlSequenceNode();

                if (node.Array.Count < 6 && !HasEnumerables(node))
                {
                    yamlNode.Style = SharpYaml.YamlStyle.Flow;
                }

                foreach (BymlNode item in node.Array)
                {
                    yamlNode.Add(SaveNode(item));
                }

                return yamlNode;
            }
            else if (node.Type == NodeType.Hash)
            {
                var yamlNode = new YamlMappingNode();

                if (node.Hash.Count < 6 && !HasEnumerables(node))
                {
                    yamlNode.Style = SharpYaml.YamlStyle.Flow;
                }

                foreach ((string key, BymlNode item) in node.Hash)
                {
                    YamlScalarNode keyNode = new YamlScalarNode(key);
                    if (IsHash(key))
                    {
                        uint hash = Convert.ToUInt32(key, 16);
                        if (Hashes.ContainsKey(hash))
                        {
                            keyNode.Value = Hashes[hash];
                        }
                    }
                    yamlNode.Add(keyNode, SaveNode(item));
                }
                return yamlNode;
            }
            else if (node.Type == NodeType.Embeded)
            {
                var yamlNode = new YamlScalarNode() { Tag = "!e", Value = Convert.ToBase64String(node.Binary) };
                return yamlNode;
            }
            else
            {
                var yamlNode = new YamlScalarNode(ConvertValue(node))
                {
                    Tag = node.Type switch
                    {
                        NodeType.UInt => "!u",
                        NodeType.Int64 => "!l",
                        NodeType.UInt64 => "!ul",
                        NodeType.Double => "!d",
                        _ => null,
                    }
                };
                return yamlNode;
            }
        }

        private static bool HasEnumerables(BymlNode node)
        {
            return node.Type switch
            {
                NodeType.Array => node.Array.Any(n => n.Type == NodeType.Array || n.Type == NodeType.Hash),
                NodeType.Hash => node.Hash.Any(p => p.Value.Type == NodeType.Array || p.Value.Type == NodeType.Hash),
                _ => false,
            };
        }

        private static string ConvertValue(BymlNode node)
        {
            return node.Type switch
            {
                NodeType.String => !string.IsNullOrEmpty(node.String) ? node.String : "''",
                NodeType.Bool => node.Bool ? "true" : "false",
                NodeType.Binary => string.Join(" ", node.Binary),
                NodeType.Int => node.Int.ToString(CultureInfo.InvariantCulture),
                NodeType.Float => FormatFloat(node.Float),
                NodeType.UInt => $"0x{node.UInt:x8}",
                NodeType.Int64 => node.Int64.ToString(CultureInfo.InvariantCulture),
                NodeType.UInt64 => $"0x{node.UInt64:x16}",
                NodeType.Double => FormatDouble(node.Double),
                _ => throw new ArgumentException($"Not value node type {node.Type}"),
            };
        }

        private static Dictionary<uint, string> Hashes => CreateHashList();
        private static Dictionary<uint, string> CreateHashList()
        {
            List<string> hashLists = new()
            {
                "AcnhByml",
                "AcnhHeaders",
                "AcnhValues"
            };

            Dictionary<uint, string> hashes = new();

            foreach (var list in hashLists) {
                string hashList = new Resource($"Data.{list}").ToString();
                foreach (string hashStr in hashList.Split('\n'))
                    CheckHash(ref hashes, hashStr);
            }

            return hashes;
        }

        private static void CheckHash(ref Dictionary<uint, string> hashes, string hashStr)
        {
            uint hash = Crc32.Compute(hashStr);
            if (!hashes.ContainsKey(hash))
                hashes.Add(hash, hashStr);
        }

        public static bool IsHash(string k) => k != null && IsHex(k.ToArray());
        private static bool IsHex(IEnumerable<char> chars)
        {
            bool isHex;

            foreach (var c in chars) {
                isHex = ((c >= '0' && c <= '9') ||
                         (c >= 'a' && c <= 'f') ||
                         (c >= 'A' && c <= 'F'));

                if (!isHex)
                    return false;
            }

            return true;
        }

        private static string FormatFloat(float f) => $"{f:0.0########}";
        private static string FormatDouble(double d) => $"{d:0.0########}";

        private static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

        private static byte[] StringToByteArray(string hex)
        {
            int NumberChars = hex.Length;
            byte[] bytes = new byte[NumberChars / 2];
            for (int i = 0; i < NumberChars; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return bytes;
        }
    }
}
