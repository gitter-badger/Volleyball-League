﻿using League.DI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TournamentManager.Data;

namespace League.Routing
{
    public class OrganizationRouteConstraint : IRouteConstraint
    {
        private readonly OrganizationContextResolver _organizationContextResolver;
        private readonly SiteList _siteList;

        public OrganizationRouteConstraint(OrganizationContextResolver organizationContextResolver, SiteList siteList)
        {
            _organizationContextResolver = organizationContextResolver;
            _siteList = siteList;
        }

        public const string Name = "ValidOrganizations";
        public bool Match(HttpContext httpContext, IRouter route, string parameterName, RouteValueDictionary values, RouteDirection routeDirection)
        {
            if (routeDirection == RouteDirection.IncomingRequest)
            {
                return SiteContext.ResolveOrganizationKey(httpContext, _siteList) != null;
            }
            // RouteDirection.UrlGeneration
            return _organizationContextResolver.Resolve(values[parameterName]?.ToString()?.ToLowerInvariant())
                       ?.OrganizationKey != null;
        }
    }
}