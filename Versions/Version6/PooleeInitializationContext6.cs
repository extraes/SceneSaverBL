namespace SceneSaverBL.Versions.Version6;

internal struct PooleeInitializationContext6
{
    public StringCollection6 barcodes;
    public Vector3 offset;

    public PooleeInitializationContext6(StringCollection6 b, Vector3 ofs)
    {
        this.barcodes = b;
        this.offset = ofs;
    }
}
