using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RentalManager.Core.Entities;

namespace RentalManager.Api.Controllers;

[ApiController]
[Authorize]
public abstract class AdminControllerBase : ControllerBase
{
    protected string UserName => User.Identity?.Name ?? "unknown";

    protected AuditLog Audit(string entity, string key, string field, string? oldValue, string? newValue) =>
        new()
        {
            EntityName = entity,
            EntityKey = key,
            FieldName = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = UserName
        };
}
