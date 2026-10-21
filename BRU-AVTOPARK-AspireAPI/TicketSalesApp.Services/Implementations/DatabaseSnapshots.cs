using System.Collections;
using System.Runtime.CompilerServices;
using SpacetimeDB.BSATN;

namespace TicketSalesApp.Services.Implementations;

/// <summary>Detach generated rows (including nested lists/bytes) before leaving the frame owner.</summary>
internal static class DatabaseSnapshots
{
    public static T Detach<T>(T value) => (T)Copy(value)!;

    private static object? Copy(object? value)
    {
        if (value == null) return null;
        var type = value.GetType();
        if (value is IStructuralReadWrite row && type.Namespace == "SpacetimeDB.Types")
        {
            using var buffer = new MemoryStream();
            using (var writer = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true)) row.WriteFields(writer);
            buffer.Position = 0;
            var result = (IStructuralReadWrite)Activator.CreateInstance(type)!;
            using var reader = new BinaryReader(buffer);
            result.ReadFields(reader);
            return result;
        }
        if (value is Array array && array.Rank == 1)
        {
            var result = (Array)array.Clone();
            for (var i = 0; i < result.Length; i++) result.SetValue(Copy(array.GetValue(i)), i);
            return result;
        }
        if (value is IList list && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var result = (IList)Activator.CreateInstance(type)!;
            foreach (var item in list) result.Add(Copy(item));
            return result;
        }
        if (value is ITuple && type.IsValueType)
        {
            var result = Activator.CreateInstance(type)!;
            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                field.SetValue(result, Copy(field.GetValue(value)));
            return result;
        }
        // Application projections contain detached scalars/DTOs, not live SDK table handles.
        return value;
    }
}
