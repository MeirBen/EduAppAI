using FamilyLearning.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FamilyLearning.Api.Infrastructure.Auth;

/// <summary>Local administrative operations that run without starting the HTTP server.</summary>
public static class ManagementCommand
{
    /// <summary>Applies migrations, performs the explicit activity-only cutover, or provisions a parent from console input.</summary>
    /// <param name="services">The root provider; database and Identity services are resolved in a new scope.</param>
    /// <param name="arguments">--migrate, --activity-only-cutover, or --create-parent followed by an email address.</param>
    /// <returns>Zero on success, or one for invalid arguments or rejected account creation.</returns>
    /// <remarks>Infrastructure failures propagate to the host. Passwords are read from input, never arguments.</remarks>
    public static async Task<int> RunAsync(IServiceProvider services, string[] arguments)
    {
        if (arguments is not ["--migrate"] and not ["--activity-only-cutover"] and not ["--create-parent", _])
        {
            Console.Error.WriteLine("Usage: --migrate | --activity-only-cutover | --create-parent parent@example.com");
            return 1;
        }
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LearningDbContext>();
        if (arguments[0] == "--activity-only-cutover")
        {
            Console.WriteLine(await ActivityOnlyCutover.RunAsync(db)
                ? "Activity-only cutover complete. Learning data and child access cleared; parent accounts and families retained."
                : "Activity-only cutover already applied. No data changed.");
            return 0;
        }
        await db.Database.MigrateAsync();
        if (arguments[0] == "--migrate") return 0;
        Console.Write("Password (12+ characters, including upper/lowercase, number and symbol): ");
        var password = ReadPassword();
        Console.Write("Confirm password: ");
        if (password != ReadPassword())
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }
        var result = await scope.ServiceProvider.GetRequiredService<ParentAccount>().CreateAsync(arguments[1], password);
        if (result.Succeeded) Console.WriteLine("Parent account created. You can now sign in.");
        else foreach (var error in result.Errors) Console.Error.WriteLine(error.Description);
        return result.Succeeded ? 0 : 1;
    }

    private static string ReadPassword()
    {
        // Redirected input supports automation without placing a password in process arguments.
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return value.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
    }
}
