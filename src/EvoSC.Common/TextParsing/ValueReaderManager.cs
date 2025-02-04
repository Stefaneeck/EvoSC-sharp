﻿using EvoSC.Common.Exceptions.Parsing;
using EvoSC.Common.Interfaces.Parsing;

namespace EvoSC.Common.TextParsing;

public class ValueReaderManager : IValueReaderManager
{
    private readonly Dictionary<Type, List<IValueReader>> _readers = new();

    public void AddReader(IValueReader reader)
    {
        foreach (var type in reader.AllowedTypes)
        {
            if (!_readers.ContainsKey(type))
            {
                _readers.Add(type, new List<IValueReader>());
            }
        
            _readers[type].Add(reader);
        }
    }

    public IEnumerable<IValueReader> GetReaders(Type type)
    {
        if (_readers.TryGetValue(type, out var readers))
        {
            return readers;
        }

        throw new ArgumentException($"Readers of type {type.Name} doesn't exist");
    }

    public async Task<object> ConvertValue(Type type, string input)
    {
        var readers = GetReaders(type);

        foreach (var reader in readers)
        {
            try
            {
                return await reader.Read(type, input);
            }
            catch (ValueConversionException)
            {
                // ignore this exception so we can try the next reader
            }
        }

        throw new FormatException();
    }
}
