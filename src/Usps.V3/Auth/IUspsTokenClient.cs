using System.Threading;
using System.Threading.Tasks;

namespace Usps.V3.Auth;

public interface IUspsTokenClient
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

