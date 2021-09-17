﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Dicom;
using EnsureThat;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Dicom.Core.Features.Common;

namespace Microsoft.Health.Dicom.Core.Features.Query
{
    internal static class QueryParamsParser
    {
        private const string IncludeFieldValueAll = "all";
        private const StringComparison QueryParameterComparision = StringComparison.OrdinalIgnoreCase;
        private delegate void ParserAction(KeyValuePair<string, StringValues> queryParam, ref QueryExpressionParams queryExpression);
        private static readonly Dictionary<string, ParserAction> ParamParsers = new Dictionary<string, ParserAction>(StringComparer.OrdinalIgnoreCase);

        static QueryParamsParser()
        {
            // register parameter parsers
            ParamParsers.Add("offset", ParseOffset);
            ParamParsers.Add("limit", ParseLimit);
            ParamParsers.Add("fuzzymatching", ParseFuzzyMatching);
            ParamParsers.Add("includefield", ParseIncludeField);
        }

        public static bool TryParse(KeyValuePair<string, StringValues> queryParam, ref QueryExpressionParams expParams)
        {
            EnsureArg.IsNotNull(expParams, nameof(expParams));
            var trimmedKey = queryParam.Key.Trim();
            if (ParamParsers.TryGetValue(trimmedKey, out ParserAction paramParser))
            {
                paramParser(queryParam, ref expParams);
                return true;
            }
            return false;
        }

        private static void ParseIncludeField(KeyValuePair<string, StringValues> queryParameter, ref QueryExpressionParams expParams)
        {
            // Check if `all` is present as one of the values in IncludeField parameter.
            if (queryParameter.Value.Any(val => IncludeFieldValueAll.Equals(val.Trim(), QueryParameterComparision)))
            {
                expParams.AllValue = true;
                return;
            }

            foreach (string paramValue in queryParameter.Value.ToArray())
            {
                foreach (string value in paramValue.Split(','))
                {
                    var trimmedValue = value.Trim();
                    if (DicomTagParser.TryParse(trimmedValue, out DicomTag dicomTag))
                    {
                        expParams.IncludeFields.Add(dicomTag);
                        continue;
                    }

                    throw new QueryParseException(string.Format(DicomCoreResource.IncludeFieldUnknownAttribute, trimmedValue));
                }
            }
        }

        private static void ParseFuzzyMatching(KeyValuePair<string, StringValues> queryParameter, ref QueryExpressionParams expParams)
        {
            var trimmedValue = queryParameter.Value.FirstOrDefault()?.Trim();
            if (bool.TryParse(trimmedValue, out bool result))
            {
                expParams.FuzzyMatch = result;
            }
            else
            {
                throw new QueryParseException(string.Format(DicomCoreResource.InvalidFuzzyMatchValue, trimmedValue));
            }
        }

        private static void ParseOffset(KeyValuePair<string, StringValues> queryParameter, ref QueryExpressionParams expParams)
        {
            var trimmedValue = queryParameter.Value.FirstOrDefault()?.Trim();
            if (int.TryParse(trimmedValue, out int result) && result >= 0)
            {
                expParams.Offset = result;
            }
            else
            {
                throw new QueryParseException(string.Format(DicomCoreResource.InvalidOffsetValue, trimmedValue));
            }
        }

        private static void ParseLimit(KeyValuePair<string, StringValues> queryParameter, ref QueryExpressionParams expParams)
        {
            var trimmedValue = queryParameter.Value.FirstOrDefault()?.Trim();
            if (int.TryParse(trimmedValue, out int result))
            {
                if (result > QueryLimit.MaxQueryResultCount || result < 1)
                {
                    throw new QueryParseException(string.Format(DicomCoreResource.QueryResultCountMaxExceeded, result, 1, QueryLimit.MaxQueryResultCount));
                }

                expParams.Limit = result;
            }
            else
            {
                throw new QueryParseException(string.Format(DicomCoreResource.InvalidLimitValue, trimmedValue));
            }
        }
    }
}