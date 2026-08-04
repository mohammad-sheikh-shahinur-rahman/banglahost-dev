using System;
using System.Threading.Tasks;
using Windows.Services.Store;

class Program
{
    static async Task Main()
    {
        try
        {
            var context = StoreContext.GetDefault();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            Console.WriteLine("Updates available: " + updates.Count);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
        }
    }
}
