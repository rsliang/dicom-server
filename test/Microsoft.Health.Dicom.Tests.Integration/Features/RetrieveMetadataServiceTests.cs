// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FellowOakDicom;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Extensions;
using Microsoft.Health.Dicom.Core.Features.Common;
using Microsoft.Health.Dicom.Core.Features.Context;
using Microsoft.Health.Dicom.Core.Features.Model;
using Microsoft.Health.Dicom.Core.Features.Partition;
using Microsoft.Health.Dicom.Core.Features.Retrieve;
using Microsoft.Health.Dicom.Core.Messages;
using Microsoft.Health.Dicom.Core.Messages.Retrieve;
using Microsoft.Health.Dicom.Tests.Common;
using Microsoft.Health.Dicom.Tests.Common.Serialization;
using Microsoft.Health.Dicom.Tests.Integration.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Dicom.Tests.Integration.Features;

public class RetrieveMetadataServiceTests : IClassFixture<DataStoreTestsFixture>
{
    private readonly RetrieveMetadataService _retrieveMetadataService;
    private readonly Func<int> _getNextWatermark;
    private readonly IInstanceStore _instanceStore;
    private readonly IMetadataStore _metadataStore;
    private readonly IETagGenerator _eTagGenerator;
    private readonly IDicomRequestContextAccessor _dicomRequestContextAccessor;

    private readonly string _studyInstanceUid = TestUidGenerator.Generate();
    private readonly string _seriesInstanceUid = TestUidGenerator.Generate();

    public RetrieveMetadataServiceTests(DataStoreTestsFixture storagefixture)
    {
        EnsureArg.IsNotNull(storagefixture, nameof(storagefixture));
        _getNextWatermark = () => storagefixture.NextWatermark;
        _instanceStore = Substitute.For<IInstanceStore>();
        _metadataStore = storagefixture.MetadataStore;
        _eTagGenerator = Substitute.For<IETagGenerator>();
        _dicomRequestContextAccessor = Substitute.For<IDicomRequestContextAccessor>();

        _dicomRequestContextAccessor.RequestContext.DataPartitionEntry = PartitionEntry.Default;

        _retrieveMetadataService = new RetrieveMetadataService(_instanceStore, _metadataStore, _eTagGenerator, _dicomRequestContextAccessor);
    }

    [Fact]
    public async Task GivenRetrieveMetadataRequestForStudy_WhenFailsToRetrieveAll_ThenNotFoundIsThrown()
    {
        using var tokenSource = new CancellationTokenSource();

        (_, VersionedDicomDataset second) = SetupDatasetList(ResourceType.Study, cancellationToken: tokenSource.Token);
        string ifNoneMatch = null;

        // Add metadata for only one instance in the given list
        await _metadataStore.StoreInstanceMetadataAsync(second.Dataset, second.Version);

        await Assert.ThrowsAsync<ItemNotFoundException>(() => _retrieveMetadataService.RetrieveStudyInstanceMetadataAsync(_studyInstanceUid, ifNoneMatch, tokenSource.Token));
    }

    [Fact]
    public async Task GivenRetrieveMetadataRequestForStudy_WhenFailsToRetrieveAny_ThenNotFoundIsThrown()
    {
        using var tokenSource = new CancellationTokenSource();

        SetupDatasetList(ResourceType.Study, cancellationToken: tokenSource.Token);
        string ifNoneMatch = null;

        await Assert.ThrowsAsync<ItemNotFoundException>(() => _retrieveMetadataService.RetrieveStudyInstanceMetadataAsync(_studyInstanceUid, ifNoneMatch, tokenSource.Token));
    }

    [Fact]
    public async Task GivenRetrieveMetadataRequestForStudy_WhenIsSuccessful_ThenInstanceMetadataIsRetrievedSuccessfully()
    {
        using var tokenSource = new CancellationTokenSource();

        (VersionedDicomDataset first, VersionedDicomDataset second) = SetupDatasetList(ResourceType.Study, cancellationToken: tokenSource.Token);
        string ifNoneMatch = null;

        // Add metadata for all instances in the given list
        await _metadataStore.StoreInstanceMetadataAsync(first.Dataset, first.Version);
        await _metadataStore.StoreInstanceMetadataAsync(second.Dataset, second.Version);

        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveStudyInstanceMetadataAsync(_studyInstanceUid, ifNoneMatch, tokenSource.Token);

        var actual = response.ResponseMetadata.ToList();
        Assert.Equal(2, actual.Count);
        ValidateResponseMetadataDataset(first.Dataset, actual[0]);
        ValidateResponseMetadataDataset(second.Dataset, actual[1]);
    }

    [Fact]
    public async Task GivenRetrieveMetadataRequestForSeries_WhenFailsToRetrieveAll_ThenNotFoundIsThrown()
    {
        using var tokenSource = new CancellationTokenSource();

        (_, VersionedDicomDataset second) = SetupDatasetList(ResourceType.Series, cancellationToken: tokenSource.Token);
        string ifNoneMatch = null;

        // Add metadata for only one instance in the given list
        await _metadataStore.StoreInstanceMetadataAsync(second.Dataset, second.Version);

        await Assert.ThrowsAsync<ItemNotFoundException>(() => _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, ifNoneMatch, tokenSource.Token));
    }

    [Fact]
    public async Task GivenRetrieveMetadataRequestForSeries_WhenFailsToRetrieveAny_ThenNotFoundIsThrown()
    {
        using var tokenSource = new CancellationTokenSource();

        SetupDatasetList(ResourceType.Series, cancellationToken: tokenSource.Token);

        string ifNoneMatch = null;
        await Assert.ThrowsAsync<ItemNotFoundException>(() => _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, ifNoneMatch, tokenSource.Token));
    }

    [Fact]
    public async Task GivenRetrieveMetadataRequestForSeries_WhenIsSuccessful_ThenInstanceMetadataIsRetrievedSuccessfully()
    {
        using var tokenSource = new CancellationTokenSource();

        (VersionedDicomDataset first, VersionedDicomDataset second) = SetupDatasetList(ResourceType.Series, cancellationToken: tokenSource.Token);

        // Add metadata for all instances in the given list
        await _metadataStore.StoreInstanceMetadataAsync(first.Dataset, first.Version);
        await _metadataStore.StoreInstanceMetadataAsync(second.Dataset, second.Version);

        string ifNoneMatch = null;
        RetrieveMetadataResponse response = await _retrieveMetadataService.RetrieveSeriesInstanceMetadataAsync(_studyInstanceUid, _seriesInstanceUid, ifNoneMatch, tokenSource.Token);

        var actual = response.ResponseMetadata.ToList();
        Assert.Equal(2, actual.Count);
        ValidateResponseMetadataDataset(first.Dataset, actual[0]);
        ValidateResponseMetadataDataset(second.Dataset, actual[1]);
    }

    // Note that tests must use unique watermarks to ensure their metadata files do not collide with each other
    private (VersionedDicomDataset First, VersionedDicomDataset Second) SetupDatasetList(
        ResourceType resourceType,
        int partitionKey = DefaultPartition.Key,
        CancellationToken cancellationToken = default)
    {
        var seriesInstanceUid = resourceType == ResourceType.Study ? TestUidGenerator.Generate() : _seriesInstanceUid;
        var result1 = new VersionedDicomDataset
        {
            Dataset = CreateValidMetadataDataset(_studyInstanceUid, seriesInstanceUid, TestUidGenerator.Generate()),
            Version = _getNextWatermark(),
        };

        var result2 = new VersionedDicomDataset
        {
            Dataset = CreateValidMetadataDataset(_studyInstanceUid, seriesInstanceUid, TestUidGenerator.Generate()),
            Version = _getNextWatermark(),
        };

        if (resourceType == ResourceType.Study)
        {
            _instanceStore
                .GetInstanceIdentifiersInStudyAsync(partitionKey, _studyInstanceUid, cancellationToken)
                .Returns(
                    new List<VersionedInstanceIdentifier>
                    {
                        result1.Dataset.ToVersionedInstanceIdentifier(version: result1.Version),
                        result2.Dataset.ToVersionedInstanceIdentifier(version: result2.Version),
                    });
        }
        else
        {
            _instanceStore
                .GetInstanceIdentifiersInSeriesAsync(partitionKey, _studyInstanceUid, seriesInstanceUid, cancellationToken)
                .Returns(
                    new List<VersionedInstanceIdentifier>
                    {
                        result1.Dataset.ToVersionedInstanceIdentifier(version: result1.Version),
                        result2.Dataset.ToVersionedInstanceIdentifier(version: result2.Version),
                    });
        }

        return (result1, result2);
    }

    private static DicomDataset CreateValidMetadataDataset(string studyInstanceUid, string seriesInstanceUid, string sopInstanceUid)
    {
        return new DicomDataset()
        {
            { DicomTag.StudyInstanceUID, studyInstanceUid },
            { DicomTag.SeriesInstanceUID, seriesInstanceUid },
            { DicomTag.SOPInstanceUID, sopInstanceUid },
        };
    }

    private static void ValidateResponseMetadataDataset(DicomDataset storedDataset, DicomDataset retrievedDataset)
    {
        // Compare result datasets by serializing.
        Assert.Equal(
            JsonSerializer.Serialize(storedDataset, AppSerializerOptions.Json),
            JsonSerializer.Serialize(retrievedDataset, AppSerializerOptions.Json));
    }

    private readonly struct VersionedDicomDataset
    {
        public DicomDataset Dataset { get; init; }

        public int Version { get; init; }
    }
}
