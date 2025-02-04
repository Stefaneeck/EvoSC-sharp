﻿using System.Diagnostics;
using System.Drawing;

namespace EvoSC.Common.Util.TextFormatting;

/// <summary>
/// Represents a single TrackMania text color.
/// </summary>
public class TextColor
{
    private readonly byte _r;
    private readonly byte _g;
    private readonly byte _b;
    
    /// <summary>
    /// Create a text color using a System.Color object.
    /// </summary>
    /// <param name="color">The color to convert to a text color.</param>
    public TextColor(Color color)
    {
        _r = (byte)Math.Round((float)color.R / 0xff * 0xf);
        _g = (byte)Math.Round((float)color.G / 0xff * 0xf);
        _b = (byte)Math.Round((float)color.B / 0xff * 0xf);
    }

    /// <summary>
    /// Create a text color using TrackMania's color representation.
    /// The numbers must be between 0-15 (inclusive) where
    /// 0 is lowest strength of the specific color, and 15 is highest.
    /// </summary>
    /// <param name="r">The red channel.</param>
    /// <param name="g">The green channel.</param>
    /// <param name="b">The blue channel.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when one of the colors are out of range.</exception>
    public TextColor(byte r, byte g, byte b)
    {
        if (r is < 0 or > 0xf || g is < 0 or > 0xf || b is < 0 or > 0xf)
        {
            throw new ArgumentOutOfRangeException("Invalid RGB colors, must be between 0 and 15.");
        }
        
        _r = r;
        _g = g;
        _b = b;
    }

    /// <summary>
    /// Convert a number to it's hex representation.
    /// </summary>
    /// <param name="n"></param>
    /// <returns></returns>
    private static string ToHex(byte n) => n switch
    {
        >= 0 and <= 9 => n.ToString(),
        10 => "a",
        11 => "b",
        12 => "c",
        13 => "d",
        14 => "e",
        15 => "f",
        _ => ""
    };
    
    /// <summary>
    /// Convert this color to the formatted text representation.
    /// </summary>
    /// <returns></returns>
    public override string ToString()
    {
        return $"${ToHex(_r)}{ToHex(_g)}{ToHex(_b)}";
    }
}
