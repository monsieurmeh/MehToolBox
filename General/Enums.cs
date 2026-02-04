using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace MehToolBox.General;

public static class Enums
{
    public static TEnum ToEnum<TEnum>(this uint uval) where TEnum : Enum { return Unsafe.As<uint, TEnum>(ref uval); }
    public static TEnum ToEnumL<TEnum>(this ulong uval) where TEnum : Enum { return Unsafe.As<ulong, TEnum>(ref uval); }
    public static uint ToUInt<TEnum>(this TEnum val) where TEnum : Enum { return Unsafe.As<TEnum, uint>(ref val); }
    public static ulong ToULong<TEnum>(this TEnum val) where TEnum : Enum { return Unsafe.As<TEnum, ulong>(ref val); }
    public static bool Any<TEnum>(this TEnum val) where TEnum : Enum { return val.ToUInt() != 0; }
    public static bool AnyL<TEnum>(this TEnum val) where TEnum : Enum { return val.ToULong() != 0UL; }
    public static bool OnlyOne<TEnum>(this TEnum val) where TEnum : Enum { uint f = val.ToUInt(); return f != 0 && (f & f - 1) == 0; }
    public static bool OnlyOneL<TEnum>(this TEnum val) where TEnum : Enum { ulong f = val.ToULong(); return f != 0UL && (f & f - 1UL) == 0UL; }
    public static bool OnlyOneOrZero<TEnum>(this TEnum val) where TEnum : Enum { uint f = val.ToUInt(); return (f & f - 1) == 0; }
    public static bool OnlyOneOrZeroL<TEnum>(this TEnum val) where TEnum : Enum { ulong f = val.ToULong(); return (f & f - 1UL) == 0UL; }
    public static bool IsSet<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToUInt() & toCheck.ToUInt()) != 0; }
    public static bool IsSetL<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToULong() & toCheck.ToULong()) != 0UL; }
    public static bool IsUnset<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToUInt() & toCheck.ToUInt()) == 0; }
    public static bool IsUnsetL<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToULong() & toCheck.ToULong()) == 0UL; }
    public static bool AnyOf<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToUInt() & toCheck.ToUInt()) != 0; }
    public static bool AnyOfL<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToULong() & toCheck.ToULong()) != 0UL; }
    public static bool AllOf<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { uint c = toCheck.ToUInt(); return (val.ToUInt() & c) == c; }
    public static bool AllOfL<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { ulong c = toCheck.ToULong(); return (val.ToULong() & c) == c; }
    public static bool OnlyOneOf<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { uint v = val.ToUInt() & toCheck.ToUInt(); return v != 0 && (v & v - 1) == 0; }
    public static bool OnlyOneOfL<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { ulong v = val.ToULong() & toCheck.ToULong(); return v != 0UL && (v & v - 1UL) == 0UL; }
    public static bool NoneOf<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToUInt() & toCheck.ToUInt()) == 0; }
    public static bool NoneOfL<TEnum>(this TEnum val, TEnum toCheck) where TEnum : Enum { return (val.ToULong() & toCheck.ToULong()) == 0UL; }
    public static bool OthersSet<TEnum>(this TEnum val, TEnum toIgnore) where TEnum : Enum { return (val.ToUInt() & ~toIgnore.ToUInt()) != 0; }
    public static bool OthersSetL<TEnum>(this TEnum val, TEnum toIgnore) where TEnum : Enum { return (val.ToULong() & ~toIgnore.ToULong()) != 0UL; }
    public static TEnum UnsetFlags<TEnum>(this TEnum val, TEnum flags) where TEnum : Enum { return (val.ToUInt() & ~flags.ToUInt()).ToEnum<TEnum>(); }
    public static TEnum UnsetFlagsL<TEnum>(this TEnum val, TEnum flags) where TEnum : Enum { return (val.ToULong() & ~flags.ToULong()).ToEnumL<TEnum>(); }
    public static TEnum SetFlags<TEnum>(this TEnum val, TEnum flags, bool shouldSet = true) where TEnum : Enum { return (shouldSet ? val.ToUInt() | flags.ToUInt() : val.ToUInt() & ~flags.ToUInt()).ToEnum<TEnum>(); }
    public static TEnum SetFlagsL<TEnum>(this TEnum val, TEnum flags, bool shouldSet = true) where TEnum : Enum { return (shouldSet ? val.ToULong() | flags.ToULong() : val.ToULong() & ~flags.ToULong()).ToEnumL<TEnum>(); }
}
