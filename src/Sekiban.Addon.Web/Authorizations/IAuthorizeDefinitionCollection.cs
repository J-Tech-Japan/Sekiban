﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Command;
namespace Sekiban.Addon.Web.Authorizations;

public interface IAuthorizeDefinitionCollection
{
    IEnumerable<IAuthorizeDefinition> Collection { get; }

    public AuthorizeResultType CheckAuthorization(
        AuthorizeMethodType authorizeMethodType,
        ControllerBase controller,
        Type aggregateType,
        Type? commandType,
        IAggregateCommand? command,
        HttpContext httpContext,
        IServiceProvider serviceProvider);
}