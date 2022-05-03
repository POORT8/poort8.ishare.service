﻿using Microsoft.AspNetCore.Mvc;
using Poort8.Ishare.Core;
using System.Text.Json;

namespace Poort8.Ishare.Service.Service;

[Route("api/[controller]")]
[ApiController]
public class ServiceController : ControllerBase
{
    private readonly ILogger<ServiceController> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authenticationService;
    private readonly IPolicyEnforcementPoint _policyEnforcementPoint;

    public ServiceController(
        ILogger<ServiceController> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IAuthenticationService authenticationService,
        IPolicyEnforcementPoint policyEnforcementPoint)
    {
        _logger = logger;
        _configuration = configuration;

        _httpClient = httpClientFactory.CreateClient(nameof(ServiceController));

        _authenticationService = authenticationService;
        _policyEnforcementPoint = policyEnforcementPoint;
    }

    //TODO: Swagger
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "delegation_evidence")] string delegationEvidence,
        [FromBody] dynamic requestBody)
    {
        var authorization = Request.Headers.Authorization;

        _logger.LogDebug("Received service POST request with authorization header: {authorization}", authorization);

        var errorResponse = HandleAuthenticationAndAuthorization(authorization, delegationEvidence);
        if (errorResponse is not null) { return errorResponse; }

        try
        {
            //NOTE: For now only json bodies
            var body = JsonContent.Create(requestBody);

            _logger.LogInformation("Sending post request to backend service with body: {body}", (string)JsonSerializer.Serialize(requestBody));

            var url = $"{_configuration["BackendUrl"]}";
            var response = await _httpClient.PostAsync(url, body);

            _logger.LogInformation("Returning status code: {statusCode}", (int)response.StatusCode);
            return new StatusCodeResult((int)response.StatusCode);
        }
        catch (Exception e)
        {
            _logger.LogError("Returning internal server error, could not post data to backend url: {msg}", e.Message);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("{id?}")]
    public async Task<IActionResult> Read(
        string? id,
        [FromHeader(Name = "delegation_evidence")] string delegationEvidence)
    {
        var authorization = Request.Headers.Authorization;

        _logger.LogDebug("Received service GET request with authorization header: {authorization}", authorization);

        var errorResponse = HandleAuthenticationAndAuthorization(authorization, delegationEvidence);
        if (errorResponse is not null) { return errorResponse; }

        try
        {
            var url = $"{_configuration["BackendUrl"]}/{id}";
            var data = await _httpClient.GetStringAsync(url);
            var jsonData = JsonDocument.Parse(data);

            _logger.LogInformation("Returning data: {data}", JsonSerializer.Serialize(jsonData));
            return new OkObjectResult(jsonData);
        }
        catch (Exception e)
        {
            _logger.LogError("Returning internal server error, could not get data at backend url: {msg}", e.Message);
            return new StatusCodeResult(StatusCodes.Status500InternalServerError);
        }
    }

    private IActionResult? HandleAuthenticationAndAuthorization(string authorization, string delegationEvidence)
    {
        if (string.IsNullOrEmpty(authorization)) { return new UnauthorizedResult(); }

        try
        {
            _authenticationService.ValidateAuthorizationHeader(_configuration["ClientId"], authorization);
        }
        catch (Exception e)
        {
            _logger.LogWarning("Returning bad request: invalid authorization header. {msg}", e.Message);
            return new UnauthorizedObjectResult("Invalid authorization header.");
        }

        _logger.LogDebug("Received delegation_evidence header: {delegationEvidence}", delegationEvidence);

        try
        {
            var isPermitted = _policyEnforcementPoint.VerifyDelegationTokenPermit(_configuration["AuthorizationRegistryIdentifier"], delegationEvidence);
            if (!isPermitted) { throw new Exception("VerifyDelegationTokenPermit returned false."); }
        }
        catch (Exception e)
        {
            _logger.LogInformation("Returning forbidden, invalid delegation evidence: {msg}", e.Message);
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        }

        _logger.LogInformation("Valid authentication and authorization.");
        return null;
    }
}
