namespace ArchIntel.Api.Pagination;

/// <summary>Opaque cursor for the cursor-pagination scheme in 05-rest-api.md Section 3.5. The
/// cursor is just a base64-wrapped offset — opaque to clients, but simple, since none of the
/// Phase 2 list endpoints have an underlying keyset to page on and the whole list already has to
/// be materialized in memory (IGraphReader has no server-side skip/take for these queries).</summary>
public static class CursorPagination
{
    public static string Encode(int offset) => Convert.ToBase64String(BitConverter.GetBytes(offset));

    public static bool TryDecode(string? cursor, out int offset)
    {
        offset = 0;
        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            if (bytes.Length != sizeof(int))
            {
                return false;
            }

            offset = BitConverter.ToInt32(bytes);
            return offset >= 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
