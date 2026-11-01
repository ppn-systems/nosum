// Copyright (c) 2026 PPN Systems. All rights reserved.

using FluentAssertions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nosum.Contracts.Journal.S2C;
using Nosum.Gameplay.Items;
using Nosum.Wasm.Socket.Journal;
using Nosum.Wasm.ViewModels.Inventory;
using Nosum.Wasm.ViewModels.Journal;
using Nosum.Wasm.ViewModels.Resources;
using Xunit;

namespace Nosum.UnitTests.Wasm;

[Collection(SharedUiCulture.Name)]
public sealed class JournalViewModelTests
{
    [Fact]
    public void Filter_Found_ShowsOnlyTheObtainedItems()
    {
        JournalViewModel vm = Open(ItemCatalog.Hoe, ItemCatalog.Axe);

        vm.SetFilter(JournalFilter.Found);

        vm.FilteredCells().Should().HaveCount(2);
        vm.FilteredCells().Should().OnlyContain(cell => cell.Discovered);
        vm.PageCells().Should().HaveCount(2);
    }

    [Fact]
    public void Filter_Missing_HidesTheObtainedItems()
    {
        JournalViewModel vm = Open(ItemCatalog.Hoe, ItemCatalog.Axe);

        vm.SetFilter(JournalFilter.Missing);

        vm.FilteredCells().Should().HaveCount(JournalViewModel.Total - 2);
        vm.FilteredCells().Should().NotContain(cell => cell.Discovered);
    }

    [Fact]
    public void Filter_All_ShowsEveryItemInCatalogueOrder()
    {
        JournalViewModel vm = Open(ItemCatalog.Hoe);

        vm.SetFilter(JournalFilter.All);

        vm.FilteredCells().Should().HaveCount(JournalViewModel.Total);
        vm.PageCount.Should().Be((JournalViewModel.Total + JournalViewModel.CellsPerPage - 1) / JournalViewModel.CellsPerPage);
    }

    [Fact]
    public void Filter_Found_FewerItemsThanOnePage_HasOnePage()
    {
        JournalViewModel vm = Open(ItemCatalog.Hoe);

        vm.SetFilter(JournalFilter.Found);

        vm.PageCount.Should().Be(1);
    }

    [Fact]
    public void SetFilter_ChangedFilter_ReturnsToTheFirstPage()
    {
        JournalViewModel vm = Open();
        vm.SetFilter(JournalFilter.All);
        vm.GoToPage(1);
        vm.Page.Should().Be(1);

        vm.SetFilter(JournalFilter.Missing);

        vm.Page.Should().Be(0);
        vm.Filter.Should().Be(JournalFilter.Missing);
    }

    [Fact]
    public void SetFilter_SameFilter_KeepsThePage()
    {
        JournalViewModel vm = Open();
        vm.SetFilter(JournalFilter.Missing);
        vm.GoToPage(1);

        vm.SetFilter(JournalFilter.Missing);

        vm.Page.Should().Be(1);
    }

    private static JournalViewModel Open(params int[] obtained)
    {
        FakeGameSocket socket = new();
        socket.ReplyWith(_ =>
        {
            JournalReply reply = new();
            foreach (int itemId in obtained)
            {
                JournalEntry entry = JournalEntry.Create();
                entry.ItemId = itemId;
                entry.Count = 1;
                entry.FirstObtainedUnix = 1_700_000_000;
                reply.Entries.Add(entry);
            }

            return reply;
        });
        JournalChannel channel = new(socket);
        channel.Load();
        return new JournalViewModel(Localizer<Language>(), Localizer<Catalogue>(), new NoIcons(), channel);
    }

    private static StringLocalizer<T> Localizer<T>()
    {
        ResourceManagerStringLocalizerFactory factory = new(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
        return new StringLocalizer<T>(factory);
    }

    private sealed class NoIcons : IItemIconSource
    {
        public bool TryGet(int itemId, out ItemIcon icon)
        {
            icon = new ItemIcon("sheet.png", 0, 0, 16, 16, 64, 64);
            return true;
        }
    }
}
