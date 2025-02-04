﻿using System.ComponentModel;
using System.Reflection;
using Config.Net;
using EvoSC.Common.Util;
using Tomlet;
using Tomlet.Models;

namespace EvoSC.Common.Config.Stores;

public class TomlConfigStore<TConfig> : IConfigStore where TConfig : class
{
    private readonly TomlDocument _document;
    private readonly string _path;
    
    public TomlConfigStore(string path)
    {
        if (!File.Exists(path))
        {
            _document = CreateDefaultConfig();
            File.WriteAllText(path, _document.SerializedValue);
        }
        else
        {
            _document = TomlParser.ParseFile(path);
        }
        
        _path = Path.GetFullPath(path);
    }

    private TomlDocument CreateDefaultConfig()
    {
        var rootType = typeof(TConfig);
        var document = BuildSubDocument(TomlDocument.CreateEmpty(), rootType, "");
        
        // avoid inline writing which is more human readable
        document.ForceNoInline = false;
        
        return document;
    }
    
    private TomlDocument BuildSubDocument(TomlDocument document, Type type, string name)
    {
        foreach (var property in type.GetProperties())
        {
            if (property.PropertyType.IsInterface)
            {
                document = BuildSubDocument(document, property.PropertyType, name == "" ? property.Name : $"{name}.{property.Name}");
            }
            else
            {
                var descAttr = property.GetCustomAttribute<DescriptionAttribute>();
                var optionAttr = property.GetCustomAttribute<OptionAttribute>();

                // get property name
                var propName = optionAttr?.Alias ?? property.Name;
                propName = name == "" ? propName : $"{name}.{propName}";
                
                // get property value
                var value = TomletMain.ValueFrom(property.PropertyType,
                    optionAttr?.DefaultValue ?? property.PropertyType.GetDefaultTypeValue());

                // add description/comment if defined
                if (descAttr != null)
                {
                    value.Comments.PrecedingComment = descAttr.Description;
                }
                
                // write to document
                document.Put(propName, value);
            }
        }
        
        return document;
    }

    public void Dispose()
    {
        // do nothing because the document lives for the entire application and is disposed on shutdown
    }

    private string GetArrayValue(string key)
    {
        var value = _document.GetValue(key) as TomlArray;

        return string.Join(" ", value.Select(v => v.StringValue));
    }
    
    public string? Read(string key)
    {
        var lastDotIndex = key.LastIndexOf(".", StringComparison.Ordinal);

        if (lastDotIndex > 0 && key.Length > lastDotIndex + 1 && !char.IsAsciiLetterOrDigit(key[lastDotIndex + 1]))
        {
            var value = _document.GetValue(key[..lastDotIndex]) as TomlArray;
            return value.Count.ToString();
        }
        else if (key.EndsWith("]"))
        {
            var indexStart = key.IndexOf("[", StringComparison.Ordinal);
            var index = int.Parse(key[(indexStart+1)..^1]);
            var value = _document.GetValue(key[..indexStart]) as TomlArray;

            return value?.Skip(index)?.FirstOrDefault()?.StringValue;
        }

        return _document.GetValue(key).StringValue;
    }

    public void Write(string key, string? value)
    {
        throw new NotImplementedException();
    }

    public bool CanRead => true;
    public bool CanWrite => false;
}
