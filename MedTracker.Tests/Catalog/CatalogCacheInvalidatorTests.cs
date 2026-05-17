using MedTracker.Application.Interfaces;
using MedTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace MedTracker.Tests.Catalog;

public class CatalogCacheInvalidatorTests
{
    [Fact]
    public async Task Invalidate_BumpsVersionStore()
    {
        var versionStore = Substitute.For<ICatalogVersionStore>();
        versionStore.BumpAsync(Arg.Any<CancellationToken>()).Returns(2L);

        var sut = new CatalogCacheInvalidator(versionStore, NullLogger<CatalogCacheInvalidator>.Instance);
        await sut.InvalidateAsync();

        await versionStore.Received(1).BumpAsync(Arg.Any<CancellationToken>());
    }
}