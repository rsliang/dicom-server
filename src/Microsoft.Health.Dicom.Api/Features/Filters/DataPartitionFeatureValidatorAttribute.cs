﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Dicom.Api.Features.Routing;
using Microsoft.Health.Dicom.Core.Configs;
using Microsoft.Health.Dicom.Core.Exceptions;

namespace Microsoft.Health.Dicom.Api.Features.Filters
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DataPartitionFeatureValidatorAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));
            var svc = context.HttpContext.RequestServices;
            IOptions<FeatureConfiguration> featureConfiguration = svc.GetService<IOptions<FeatureConfiguration>>();
            var isPartitionEnabled = featureConfiguration.Value.EnableDataPartitions;
            RouteData routeData = context.RouteData;

            if (routeData?.Values != null)
            {
                if (isPartitionEnabled)
                {
                    if (!routeData.Values.TryGetValue(KnownActionParameterNames.PartitionId, out var partitionId) || string.IsNullOrWhiteSpace(partitionId?.ToString()))
                    {
                        throw new DataPartitionsMissingPartitionException();
                    }
                }
                else
                {
                    if (routeData.Values.TryGetValue(KnownActionParameterNames.PartitionId, out var partitionId) && !string.IsNullOrWhiteSpace(partitionId?.ToString()))
                    {
                        throw new DataPartitionsFeatureDisabledException();
                    }
                }
            }

        }
    }
}
