﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dicom;
using EnsureThat;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.ExtendedQueryTag;
using Microsoft.Health.Dicom.SqlServer.Features.Schema;
using Microsoft.Health.Dicom.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Dicom.Tests.Integration.Persistence
{
    /// <summary>
    /// Basic performance tests to detect regression
    /// </summary>
    public class PerformanceTests
    {
        private readonly int _currentSchemaVersion = SchemaVersionConstants.Max;
        private readonly int _previousSchemaVersion = SchemaVersionConstants.Max - 1;
        private readonly Dictionary<int, SqlDataStoreTestsFixture> _fixtures = new Dictionary<int, SqlDataStoreTestsFixture>();
        private readonly ITestOutputHelper _testOutputHelper;

        private readonly Random _rng = new Random();

        private const int CreateInstances = 5000;

        public PerformanceTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = EnsureArg.IsNotNull(testOutputHelper);
        }


        [Fact]
        public async Task CurrentAndLastSchemaVersions_HaveSimilarStorePerformance()
        {
            await InitializeFixtures();

            var schemaVersionPerformance = new Dictionary<int, TimeSpan>();

            var instances = new List<DicomDataset>();
            for (var i = 0; i < CreateInstances; i++)
            {
                var dataset = Samples.CreateRandomInstanceDataset();
                instances.Add(dataset);
            }

            foreach ((var version, var fixture) in _fixtures)
            {
                var stopwatch = Stopwatch.StartNew();

                for (var i = 0; i < CreateInstances; i++)
                {
                    await fixture.IndexDataStore.BeginCreateInstanceIndexAsync(instances[i], new List<QueryTag>());
                }

                for (var i = 0; i < CreateInstances; i++)
                {
                    var instanceIdentifier = instances[_rng.Next(CreateInstances)].ToInstanceIdentifier();
                    await fixture.InstanceStore.GetInstanceIdentifiersInSeriesAsync(instanceIdentifier.StudyInstanceUid, instanceIdentifier.SeriesInstanceUid);
                }

                stopwatch.Stop();
                schemaVersionPerformance.Add(version, stopwatch.Elapsed);
            }

            var diff = schemaVersionPerformance[_currentSchemaVersion] - schemaVersionPerformance[_previousSchemaVersion];

            var diffPercentage = diff / schemaVersionPerformance[_currentSchemaVersion];
            var marginOfError = 0.02;

            _testOutputHelper.WriteLine($"current: {schemaVersionPerformance[_currentSchemaVersion]}, previous: {schemaVersionPerformance[_previousSchemaVersion]}, diff: {diffPercentage}");

            Assert.True(diffPercentage < marginOfError, $"current: {schemaVersionPerformance[_currentSchemaVersion]}, previous: {schemaVersionPerformance[_previousSchemaVersion]}, diff: {diffPercentage}");
        }

        private async Task<IReadOnlyList<int>> AddExtendedQueryTagsAsync(
            SqlDataStoreTestsFixture fixture,
            IEnumerable<AddExtendedQueryTagEntry> extendedQueryTagEntries,
            int maxAllowedCount = 128,
            bool ready = true,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExtendedQueryTagStoreEntry> tags = await fixture.ExtendedQueryTagStore.AddExtendedQueryTagsAsync(
                extendedQueryTagEntries,
                maxAllowedCount,
                ready: ready,
                cancellationToken: cancellationToken);

            return tags.Select(x => x.Key).ToList();
        }

        private async Task RunTest(Action testMethod, string message)
        {
            foreach ((var version, var fixture) in _fixtures)
            {
                var stopwatch = Stopwatch.StartNew();

                for (var i = 0; i < CreateInstances; i++)
                {
                    await fixture.invoke .IndexDataStore.BeginCreateInstanceIndexAsync(instances[i], new List<QueryTag>());
                }

                for (var i = 0; i < CreateInstances; i++)
                {
                    var instanceIdentifier = instances[_rng.Next(CreateInstances)].ToInstanceIdentifier();
                    await fixture.InstanceStore.GetInstanceIdentifiersInSeriesAsync(instanceIdentifier.StudyInstanceUid, instanceIdentifier.SeriesInstanceUid);
                }

                stopwatch.Stop();
                schemaVersionPerformance.Add(version, stopwatch.Elapsed);
            }

            var diff = schemaVersionPerformance[_currentSchemaVersion] - schemaVersionPerformance[_previousSchemaVersion];

            var diffPercentage = diff / schemaVersionPerformance[_currentSchemaVersion];
            var marginOfError = 0.02;

            _testOutputHelper.WriteLine($"current: {schemaVersionPerformance[_currentSchemaVersion]}, previous: {schemaVersionPerformance[_previousSchemaVersion]}, diff: {diffPercentage}");

        }

        private async Task InitializeFixtures()
        {
            var currentSchemaInformation = new SchemaInformation(SchemaVersionConstants.Min, _currentSchemaVersion);
            var previousSchemaInformation = new SchemaInformation(SchemaVersionConstants.Min, _previousSchemaVersion);

            var currentFixture = new SqlDataStoreTestsFixture(SqlDataStoreTestsFixture.GenerateDatabaseName("PERFCURRENT"), currentSchemaInformation);
            var previousFixture = new SqlDataStoreTestsFixture(SqlDataStoreTestsFixture.GenerateDatabaseName("PERFPREVIOUS"), previousSchemaInformation);

            await currentFixture.InitializeAsync(false);
            await previousFixture.InitializeAsync(false);

            _fixtures.Add(_currentSchemaVersion, currentFixture);
            _fixtures.Add(_previousSchemaVersion, previousFixture);
        }
    }
}
