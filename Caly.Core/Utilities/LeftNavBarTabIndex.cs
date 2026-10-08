namespace Caly.Core.Utilities
{
    // DocumentTabViewTabIndexTests.EnsureTabIndexValuesAreCorrect() makes sure the enum values
    // match the tab order in DocumentTabView.axaml. If you change this enum, update the test.

    internal enum LeftNavBarTabIndex : int
    {
        Thumbnails = 0,

        /// <summary>
        /// aka Outlines.
        /// </summary>
        Bookmarks = 1,
        Search = 2,
        DocumentProperties = 3,
        EmbeddedFiles = 4,
        Layers = 5
    }
}
