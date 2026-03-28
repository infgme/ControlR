using ControlR.Libraries.Api.Contracts.Constants;
using ControlR.Libraries.Api.Contracts.Dtos;
using ControlR.Libraries.Api.Contracts.Dtos.ServerApi;
using ControlR.Libraries.Api.Contracts.Enums;
using System.Net.Http.Json;

namespace ControlR.ApiClient;

public partial class ControlrApi
{
  async Task<ApiResult<BundleMetadataDto>> IAgentUpdateApi.GetBundleMetadata(RuntimeId runtime, CancellationToken cancellationToken)
  {
    return await ExecuteApiCall(async () =>
      await _client.GetFromJsonAsync<BundleMetadataDto>(
        $"{HttpConstants.AgentUpdateEndpoint}/get-bundle-metadata/{runtime}",
        cancellationToken));
  }

  async Task<ApiResult<string>> IAgentUpdateApi.GetCurrentAgentHashSha256(RuntimeId runtime, CancellationToken cancellationToken)
  {
    return await ExecuteApiCall(async () => await _client.GetStringAsync(
      $"{HttpConstants.AgentUpdateEndpoint}/get-hash-sha256/{runtime}",
      cancellationToken));
  }
}
