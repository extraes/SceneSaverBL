namespace SceneSaverBL.Interfaces;

internal interface ISaveFile : ISerializableClass
{
    public byte Version { get; }
    public Task SetFilePath(string filePath); // called before bonemenu init
    public bool ExistsOnDisk();
    public void PopulateBoneMenu(Page category);
    public Task Construct(Poolee[] poolees, ConstraintTracker[] constraints);
    public Task Initialize();
    public string ToString();
    public (Bounds display, Bounds elementBounds) GetBoundsForDupeAndDisplay();
}
