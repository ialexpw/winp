using System.Diagnostics;
using System.Threading.Tasks;
using Winp.Configuration;

namespace Winp
{
    public interface IPackage
    {
        string Name { get; }

        ProcessStartInfo ConfigureStart(ApplicationConfig application);
        ProcessStartInfo ConfigureStop(ApplicationConfig application, int pid);
        Task<string?> Install(ApplicationConfig application);
        bool IsReady(ApplicationConfig application);
    }
}