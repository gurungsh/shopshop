using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Catalog.Api.Services;

internal static class DbErrors
{
    // Postgres 23505 = unique_violation; catches the race the pre-save duplicate checks can't.
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
