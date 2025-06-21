// ───────────────────────────────────────────────────────────────────────────
// PBLimiter Section
// ───────────────────────────────────────────────────────────────────────────

bool PBLIMITER_PAUSE_ANYWAY      = true;   // If we get an unknown shutdown code, still pause
int  PBLIMITER_COOLDOWN_SECONDS = 60;     // Cooldown duration (seconds)

string   PBLNote;
DateTime PBLTimestamp;

void PBLStartCooldown(string reason = "Unknown", int seconds = -1)
{
    if (seconds < 0) seconds = PBLIMITER_COOLDOWN_SECONDS;
    PBLTimestamp = DateTime.UtcNow.AddSeconds(seconds);
    PBLNote      = reason;
}

bool PBLimiter(string argument)
{
    // 1) Replace "argument?.StartsWith" with a normal null check + StartsWith
    if (argument != null && argument.StartsWith("GracefulShutDown"))
    {
        // 2) Declare 'code' outside, then use TryParse(out code)
        var parts = argument.Split(new[] { "::" }, StringSplitOptions.None);
        int code;
        if (parts.Length > 1 && int.TryParse(parts[1], out code))
        {
            switch (code)
            {
                case -1:
                    PBLStartCooldown("Exceeded Runtime Limit");
                    break;
                case -2:
                    PBLStartCooldown("Exceeded Average Runtime");
                    break;
                case -3:
                    PBLStartCooldown("Exceeded Combined Limit!");
                    Echo("Exceeded Combined Limit!\n- All PBs paused for punishment.\n");
                    break;
                default:
                    if (code > 0)
                        PBLStartCooldown("Grace period granted", code);
                    else if (PBLIMITER_PAUSE_ANYWAY)
                        PBLStartCooldown("Unknown code; pausing anyway");
                    break;
            }
        }
        else
        {
            if (PBLIMITER_PAUSE_ANYWAY)
                PBLStartCooldown("Unknown shutdown request; pausing anyway");
        }
    }

    // 3) If no cooldown has been set, return false → run normal logic
    if (string.IsNullOrEmpty(PBLNote))
        return false;

    // 4) If still within the cooldown window, show remaining time and bail out
    if (DateTime.UtcNow < PBLTimestamp)
    {
        double secsLeft = (PBLTimestamp - DateTime.UtcNow).TotalSeconds;
        Echo($"Cooldown: {secsLeft:F0}s\nReason: {PBLNote}");
        return true;
    }

    // 5) Cooldown expired—clear note so next call returns false
    PBLNote = "";
    return false;
}
// ───────────────────────────────────────────────────────────────────────────
// End PBLimiter Section
// ───────────────────────────
// --------------
// LOCK HELPERS
// --------------

/// The maximum time (in seconds) we'll hold the lock before auto‐release:
const int LOCK_TIMEOUT = 60;

/// A simple struct to parse/read our CustomData lock info
struct LockInfo
{
    public string Owner;
    public DateTime ExpiresUtc;
}

/// Attempt to acquire the lock on HoldingA & HoldingB.
/// Returns true if we now hold it; false if someone else still does.
bool AcquireLock()
{
    // Read the raw CD from HoldingA (you could pick either HoldingA or B to store the flag)
    string cd = _holdingA.CustomData;
    LockInfo existing;
    if (TryParseLock(cd, out existing))
    {
        // There is a current lock; is it still in the future?
        if (DateTime.UtcNow < existing.ExpiresUtc)
            return false; // somebody else has it
    }

    // Otherwise the lock is free (or expired).  Grab it for LOCK_TIMEOUT seconds.
    var mine = new LockInfo {
        Owner      = Runtime.UpdateFrequency.ToString(), // e.g. use script name or PB name
        ExpiresUtc = DateTime.UtcNow.AddSeconds(LOCK_TIMEOUT)
    };
    _holdingA.CustomData = FormatLock(mine);
    return true;
}

/// Releases whatever lock is in CustomData (no questions asked).
void ReleaseLock()
{
    _holdingA.CustomData = "";
}

/// Try to parse a CustomData string into a LockInfo.  Returns false if empty or invalid.
bool TryParseLock(string cd, out LockInfo info)
{
    info = default(LockInfo);
    if (string.IsNullOrEmpty(cd)) return false;

    // Expecting two lines: Owner:<owner> and Expires:<ISO-8601 timestamp>
    var lines = cd.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length < 2) return false;

    // line[0] == "Owner:Foo" ; line[1] == "Expires:2025-06-05T12:34:56Z"
    var oParts = lines[0].Split(new[] {':'},2);
    var eParts = lines[1].Split(new[] {':'},2);
    if (oParts.Length!=2 || eParts.Length!=2) return false;

    DateTime dt;
    if (!DateTime.TryParse(eParts[1].Trim(), out dt)) return false;

    info = new LockInfo {
        Owner      = oParts[1].Trim(),
        ExpiresUtc = dt
    };
    return true;
}

/// Produce the two-line CustomData from a LockInfo
string FormatLock(LockInfo info)
{
    // Use the round-trip "o" format for full ISO 8601 UTC
    return $"Owner:{info.Owner}\nExpires:{info.ExpiresUtc.ToString("o")}";
}
///////////////////////////////
// 1) CONSTANTS AND MENU NAMES 
///////////////////////////////

const int VISIBLE_ITEM_COUNT = 8;
const int IDLE_THRESHOLD     = 15;
const int TICK_THRESHOLD     = 10;
const float BASE_SCALE       = 0.7f;
const float TEXT_SCALE       = 0.6f;
const float HEADER_SCALE     = 1.0f;
const float ICON_SIZE        = 64f;
const float ITEM_SPACING     = 45f;
const float SCROLL_INDICATOR_OFFSET = 30f;

const string DEFAULT_CURRENCY_TYPE    = "Tech8x";
const string DEFAULT_CURRENCY_DISPLAY = "Exotic Tech";

static class MenuNames
{
    public const string Welcome              = "Welcome";
    public const string TradeMode            = "Trade Mode"; 
    public const string BuyMode              = "Buy Mode";
    public const string SellMode             = "Sell Mode";
    public const string BuyIngots            = "Buy Ingots";
    public const string BuyComponents        = "Buy Components";
    public const string BuyModdedComponents  = "Buy Modded Components";
    public const string BuyAmmunition        = "Buy Ammunition";
    public const string SellIngots           = "Sell Ingots";
    public const string SellComponents       = "Sell Components";
    public const string SellModdedComponents = "Sell Modded Components";
    public const string SellAmmunition       = "Sell Ammunition";
    public const string Exchange             = "Exchange";
    public const string Confirmation         = "Confirmation";
    public const string InsufficientTech     = "InsufficientTech";
    public const string InsufficientStock    = "InsufficientStock";
}

////////////////////////////////////////////////////////////////
// 2) GLOBAL BLOCKS AND VARIABLES (existing + new sorter/holding)
////////////////////////////////////////////////////////////////

IMyTextPanel      lcdCustomerScreen;
IMyTextPanel      debugLCD;
IMyTextPanel      adminLCD;
IMyButtonPanel    buttonPanel;
IMyCargoContainer TradeContainer;
IMyCargoContainer techVault;
IMyShipConnector customerConnector;
IMyTextPanel      lcdIngotPrices;
IMyTextPanel      lcdComponentPrices;
IMyTextPanel      lcdModdedComponentPrices;
IMyTextPanel      lcdAmmoPrices;
IMyTextPanel      lcdTradePreview;
List<IMyCargoContainer> storeContainers = new List<IMyCargoContainer>(); // Only containers in "STORE INVENTORY" group

// --- NEW: Sorters & Holdings for staging ---
IMyConveyorSorter _sorterA; // “Sorter StorageToHoldingA”
IMyConveyorSorter _sorterB; // “Sorter HoldingAToTrade”
IMyConveyorSorter _sorterC; // “Sorter TradeToHoldingB”
IMyConveyorSorter _sorterD; // “Sorter HoldingBToVaultOrStore”
IMyCargoContainer   _holdingA; // “Holding A”
IMyCargoContainer   _holdingB; // “Holding B”

// Ammo Scrolling
int  ammoPageIndex   = 0;
bool ammoScrollDown  = true;
const int visibleAmmoLines = 18; // Adjust based on your LCD’s height/font size

// Cache variables for inventory
Dictionary<string, long> cachedStoreItems = new Dictionary<string, long>();
int cachedTechAmount = 0;

////////////////////////////////////////////////////////////////
// 3) MENU STATE AND TIMING (unchanged)
////////////////////////////////////////////////////////////////

string currentMenu = MenuNames.Welcome;
int    selectedIndex = 0;
string selectedItem  = "";
Stack<string> menuStack = new Stack<string>();

int  firstVisibleIndex = 0;
bool debugMode         = false;
int  idleCounter       = 0;
int  tickCounter       = 0;

int savedExchangeIndex  = 0;
int savedExchangeScroll = 0;

// Confirmation / Transaction
string confirmationMessage = "";
int    pendingTechAmount   = 0;
bool   transactionPending  = false;

// “Dummy” vars for price‐table calls that do NOT need paging.
int   dummyPageIdx   = 0;
bool  dummyDown      = false;
int   ref0           = 0;
bool  downwardsRef   = false;

// Trade Mode flag
bool isBuyMode = true;

// Pricing Dictionaries & Inventory Limits (unchanged)
Dictionary<string, double> buyPrices    = new Dictionary<string, double>();
Dictionary<string, double> sellPrices   = new Dictionary<string, double>();
Dictionary<string, long>   maxIngotLimits = new Dictionary<string, long>();
Dictionary<string, long>   maxComponentLimits = new Dictionary<string, long>();
Dictionary<string, long>   maxModdedComponentLimits = new Dictionary<string, long>();
Dictionary<string, long>   maxAmmoLimits = new Dictionary<string, long>();

// Helper class: PriceEntry (unchanged)
class PriceEntry
{
    public string Key;
    public double Buy;
    public double Sell;
    public long   Limit;
    public PriceEntry(string key, double buy, double sell, long limit)
    {
        Key   = key;
        Buy   = buy;
        Sell  = sell;
        Limit = limit;
    }
}

PriceEntry[] IngotPrices = new[] {
    new PriceEntry("Iron",       1000000, 1200000,   4500000000L),
    new PriceEntry("Gold",       7300,    8800,      750000000L),
    new PriceEntry("Silver",     75000,   90000,     1000000000L),
    new PriceEntry("Nickel",     500000,  600000,    500000000L),
    new PriceEntry("Cobalt",     200000,  240000,    1500000000L),
    new PriceEntry("Platinum",   1600,    1900,      10000000L),
    new PriceEntry("Silicon",    600000,  720000,    2500000000L),
    new PriceEntry("Magnesium",  6600,    7900,      10000000L),
    new PriceEntry("Uranium",    2000,    2400,      50000000L)
};

PriceEntry[] ComponentPrices = new[] {
    new PriceEntry("Computer",         76400,  92000,   5000000L),
    new PriceEntry("Construction",     79600,  96000,   5000000L),
    new PriceEntry("Detector",         18200,  22000,    500000L),
    new PriceEntry("Display",          68300,  82000,   500000L),
    new PriceEntry("Explosives",       1700,   2040,     500000L),
    new PriceEntry("Girder",           106000, 127000,   5000000L),
    new PriceEntry("GravityGenerator", 311,    374,      500000L),
    new PriceEntry("InteriorPlate",    212000, 254000,   5000000L),
    new PriceEntry("LargeTube",        21000,  25000,    500000L),
    new PriceEntry("Medical",          1365,   1638,     50000L),
    new PriceEntry("MetalGrid",        17000,  20000,    5000000L),
    new PriceEntry("Motor",            21000,  25000,    5000000L),
    new PriceEntry("PowerCell",        41000,  49000,    5000000L),
    new PriceEntry("RadioCommunication",66000, 79000,   500000L),
    new PriceEntry("Reactor",          7800,   9400,    5000000L),
    new PriceEntry("SmallTube",        127000, 152000,   5000000L),
    new PriceEntry("SolarCell",        40000,  48000,    500000L),
    new PriceEntry("SteelPlate",       30000,  36000,    50000000L),
    new PriceEntry("Superconductor",   2200,   2600,     1000000L),
    new PriceEntry("Thrust",           1400,   1700,     2500000L),
    new PriceEntry("ZoneChip",         0.4,    0.143,    3500L),
    new PriceEntry("BulletproofGlass", 25500,  31000,    500000L)
};

PriceEntry[] ModdedComponentPrices = new[] {
    new PriceEntry("AdaptiveDynoCapacitor",      0.25,   0.50,    50000L),
    new PriceEntry("AryxLynxon_FusionComponent", 86,     104,      10000L),
    new PriceEntry("GammaMeshRefractor",        10,     20,      25000L),
    new PriceEntry("GraphineGrid",              0.25,   0.334,   10000L),
    new PriceEntry("LeapTube",                  2.0,    4.0,     10000L),
    new PriceEntry("QuantumCoProcessor",        1,      2,       50000L),
    new PriceEntry("ShieldComponent",           88,     106,     500000L)
};

PriceEntry[] AmmoPrices = new[] {
    new PriceEntry("AutocannonClip",           1614, 1937, 100000L),
    new PriceEntry("Bolter8Inch",               659, 791, 100000L),
    new PriceEntry("C300AmmoAP",                212, 255,  100000L),
    new PriceEntry("C300AmmoG",                 196, 236,  100000L),
    new PriceEntry("C300AmmoHE",                171, 206,  100000L),
    new PriceEntry("C30Ammo",                   803, 964, 100000L),
    new PriceEntry("C30DUammo",                 704, 845, 100000L),
    new PriceEntry("C400AmmoAP",                161, 194,  100000L),
    new PriceEntry("C400AmmoCluster",           116, 140,  100000L),
    new PriceEntry("C400AmmoHE",                133, 160,  100000L),
    new PriceEntry("C500AmmoAP",                95,  114,  100000L),
    new PriceEntry("C500AmmoCasaba",            13,  16,   10000L),
    new PriceEntry("C500AmmoHE",                67,  81,  100000L),
    new PriceEntry("CRAM30mmAmmo",              679, 815, 100000L),
    new PriceEntry("DestroyerMissileMk1",       677, 813, 100000L),
    new PriceEntry("DestroyerMissileX",         89,  107,  100000L),
    new PriceEntry("H203Ammo",                  747, 897, 100000L),
    new PriceEntry("H203AmmoAP",                1130,1356, 100000L),
    new PriceEntry("LargeCalibreAmmo",          778, 934, 100000L),
    new PriceEntry("LargeRailgunAmmo",          536, 644, 100000L),
    new PriceEntry("MA_150mm",                  203, 244,  100000L),
    new PriceEntry("MA_30mm",                   1589,1907, 100000L),
    new PriceEntry("MA_Missile",                131, 158,  100000L),
	new PriceEntry("MediumCalibreAmmo",         2630, 3156,  100000L),
    new PriceEntry("Missile200mm",              1025,1230, 100000L),
    new PriceEntry("MXA_ArcherPods_Ammo",       4,   5,    10000L),
    new PriceEntry("MXA_ArcherPods_KineticAmmo",7,   9,   10000L),
    new PriceEntry("MXA_BreakWater_APAmmo",     936,1124, 100000L),
    new PriceEntry("MXA_BreakWater_GAmmo",      150, 180,  100000L),
    new PriceEntry("MXA_BreakWater_HEAmmo",     130, 156,  100000L),
    new PriceEntry("MXA_Coil155APAmmo",         749, 899, 100000L),
    new PriceEntry("MXA_Coil155HEAmmo",         104, 125,  100000L),
    new PriceEntry("MXA_Coil270Ammo",           2450,2940, 100000L),
    new PriceEntry("MXA_Coil270HEAmmo",         314, 377,  100000L),
    new PriceEntry("MXA_Coil305Ammo",           2022,2427, 100000L),
    new PriceEntry("MXA_Coil305GAmmo",          301, 362,  100000L),
    new PriceEntry("MXA_Coil305HEAmmo",         260, 312,  100000L),
    new PriceEntry("MXA_CoilgunPD_Ammo",        326, 392,  100000L),
    new PriceEntry("MXA_M58ArcherPods_Ammo",    8,   10,   10000L),
    new PriceEntry("MXA_M58ArcherPods_KineticAmmo",25, 30,   10000L),
    new PriceEntry("MXA_MACL_Ammo",             61,  74,  10000L),
    new PriceEntry("MXA_MACL_S_Ammo",           276, 332,  10000L),
    new PriceEntry("MXA_Sabre_Coilgun_Ammo",    538, 646,  10000L),
    new PriceEntry("MXA_Shiva_Ammo",            13,  16,   10000L),
    new PriceEntry("MXA_SMAC_Ammo",             119, 143,  100000L),
    new PriceEntry("NovaPlasmaInjectorPack",    83,  100,  100000L),
    new PriceEntry("PaladinCompressor",         323, 388,  100000L),
    new PriceEntry("R150ammo",                  561, 674, 100000L),
    new PriceEntry("R250ammo",                  280, 336,  100000L),
    new PriceEntry("R75ammo",                   2183,2620, 100000L),
    new PriceEntry("SmallRailgunAmmo",          3538,4246, 100000L),
    new PriceEntry("SwarmMissile50mm",          825, 990, 100000L),
    new PriceEntry("TorpedoMk1",                80,  96,  10000L)
};

/// <summary>
/// Registers every PriceEntry array into your three dictionaries.
/// </summary>
void RegisterPrices(
    PriceEntry[] entries,
    Dictionary<string,double> buyDict,
    Dictionary<string,double> sellDict,
    Dictionary<string,long>   limitDict
)
{
    foreach (var e in entries)
    {
        buyDict[e.Key]   = e.Buy;
        sellDict[e.Key]  = e.Sell;
        limitDict[e.Key] = e.Limit;
    }
}

void SetCustomPricing()
{
    RegisterPrices(IngotPrices,           buyPrices, sellPrices, maxIngotLimits);
    RegisterPrices(ComponentPrices,       buyPrices, sellPrices, maxComponentLimits);
    RegisterPrices(ModdedComponentPrices, buyPrices, sellPrices, maxModdedComponentLimits);
    RegisterPrices(AmmoPrices,            buyPrices, sellPrices, maxAmmoLimits);
}

/////////////////
// 4) MENU LISTS 
/////////////////

Dictionary<string, List<string>> menus = new Dictionary<string, List<string>>()
{
    { MenuNames.Welcome,      new List<string>() {} },
    { MenuNames.TradeMode,    new List<string>() { "Buy", "Sell" } },
    { MenuNames.BuyMode,      new List<string>() { MenuNames.BuyIngots,
                                                  MenuNames.BuyComponents,
                                                  MenuNames.BuyModdedComponents,
                                                  MenuNames.BuyAmmunition } },
    { MenuNames.SellMode,     new List<string>() { MenuNames.SellIngots,
                                                  MenuNames.SellComponents,
                                                  MenuNames.SellModdedComponents,
                                                  MenuNames.SellAmmunition } },

    // Ingots
    {
        MenuNames.BuyIngots, new List<string>()
        {
            "Iron", "Gold", "Silver", "Nickel", "Cobalt", "Platinum",
            "Silicon", "Magnesium", "Uranium", "Back to Main"
        }
    },
    {
        MenuNames.SellIngots, new List<string>()
        {
            "Iron", "Gold", "Silver", "Nickel", "Cobalt", "Platinum",
            "Silicon", "Magnesium", "Uranium", "Back to Main"
        }
    },

    // Components
    {
        MenuNames.BuyComponents, new List<string>()
        {
            "BulletproofGlass","Computer","Construction","Detector","Display","Explosives","Girder",
            "GravityGenerator","InteriorPlate","LargeTube","Medical","MetalGrid","Motor","PowerCell",
            "RadioCommunication","Reactor","SmallTube","SolarCell","SteelPlate","Superconductor","Thrust","Back to Main"
        }
    },
    {
        MenuNames.SellComponents, new List<string>()
        {
            "ZoneChip","BulletproofGlass","Computer","Construction","Detector","Display","Explosives","Girder",
            "GravityGenerator","InteriorPlate","LargeTube","Medical","MetalGrid","Motor","PowerCell",
            "RadioCommunication","Reactor","SmallTube","SolarCell","SteelPlate","Superconductor","Thrust",
            "Back to Main"
        }
    },

    // Modded Components
    {
        MenuNames.BuyModdedComponents, new List<string>()
        {
            "AdaptiveDynoCapacitor","AryxLynxon_FusionComponent","ShieldComponent",
            "GammaMeshRefractor","GraphineGrid","LeapTube","QuantumCoProcessor","Back to Main"
        }
    },
    {
        MenuNames.SellModdedComponents, new List<string>()
        {
            "AdaptiveDynoCapacitor","AryxLynxon_FusionComponent","ShieldComponent",
            "GammaMeshRefractor","GraphineGrid","LeapTube","QuantumCoProcessor","Back to Main"
        }
    },

    // Ammunition
    {
        MenuNames.BuyAmmunition, new List<string>()
        {
            "LargeCalibreAmmo","MediumCalibreAmmo","AutocannonClip","Bolter8Inch",
            "C300AmmoAP","C300AmmoG","C300AmmoHE","C30Ammo","C30DUammo","C400AmmoAP","C400AmmoCluster",
            "C400AmmoHE","C500AmmoAP","C500AmmoCasaba","C500AmmoHE","CRAM30mmAmmo","DestroyerMissileMk1",
            "DestroyerMissileX","H203Ammo","H203AmmoAP","LargeRailgunAmmo","MA_150mm","MA_30mm","MA_Missile",
            "Missile200mm","MXA_ArcherPods_Ammo","MXA_ArcherPods_KineticAmmo","MXA_BreakWater_APAmmo",
            "MXA_BreakWater_GAmmo","MXA_BreakWater_HEAmmo","MXA_Coil155HEAmmo","MXA_Coil270Ammo",
            "MXA_Coil270HEAmmo","MXA_Coil305Ammo","MXA_Coil305GAmmo","MXA_Coil305HEAmmo","MXA_CoilgunPD_Ammo",
            "MXA_M58ArcherPods_Ammo","MXA_M58ArcherPods_KineticAmmo","MXA_MACL_Ammo","MXA_MACL_S_Ammo",
            "MXA_Sabre_Coilgun_Ammo","MXA_Shiva_Ammo","MXA_SMAC_Ammo","NovaPlasmaInjectorPack",
            "PaladinCompressor","R150ammo","R250ammo","R75ammo","SmallRailgunAmmo","SwarmMissile50mm",
            "TorpedoMk1","Back to Main"
        }
    },
    {
        MenuNames.SellAmmunition, new List<string>()
        {
            "LargeCalibreAmmo","MediumCalibreAmmo","AutocannonClip","Bolter8Inch",
            "C300AmmoAP","C300AmmoG","C300AmmoHE","C30Ammo","C30DUammo","C400AmmoAP","C400AmmoCluster",
            "C400AmmoHE","C500AmmoAP","C500AmmoCasaba","C500AmmoHE","CRAM30mmAmmo","DestroyerMissileMk1",
            "DestroyerMissileX","H203Ammo","H203AmmoAP","LargeRailgunAmmo","MA_150mm","MA_30mm","MA_Missile",
            "Missile200mm","MXA_ArcherPods_Ammo","MXA_ArcherPods_KineticAmmo","MXA_BreakWater_APAmmo",
            "MXA_BreakWater_GAmmo","MXA_BreakWater_HEAmmo","MXA_Coil155HEAmmo","MXA_Coil270Ammo",
            "MXA_Coil270HEAmmo","MXA_Coil305Ammo","MXA_Coil305GAmmo","MXA_Coil305HEAmmo","MXA_CoilgunPD_Ammo",
            "MXA_M58ArcherPods_Ammo","MXA_M58ArcherPods_KineticAmmo","MXA_MACL_Ammo","MXA_MACL_S_Ammo",
            "MXA_Sabre_Coilgun_Ammo","MXA_Shiva_Ammo","MXA_SMAC_Ammo","NovaPlasmaInjectorPack",
            "PaladinCompressor","R150ammo","R250ammo","R75ammo","SmallRailgunAmmo","SwarmMissile50mm",
            "TorpedoMk1","Back to Main"
        }
    },

    { MenuNames.Confirmation,       new List<string>() { "Yes", "No" } },
    { MenuNames.InsufficientTech,   new List<string>() { "Back" } },
    { MenuNames.InsufficientStock,  new List<string>() { "Back" } }
};

////////////////////////////////////////////////
// 5) INVERSE MAPPING: FRIENDLY NAME → RAW KEY 
////////////////////////////////////////////////

Dictionary<string, string> itemSubtypeIds = new Dictionary<string, string>()
{
    // Ingots
    { "Iron", "Iron" },
    { "Gold", "Gold" },
    { "Silver", "Silver" },
    { "Nickel", "Nickel" },
    { "Cobalt", "Cobalt" },
    { "Platinum", "Platinum" },
    { "Silicon", "Silicon" },
    { "Magnesium", "Magnesium" },
    { "Uranium", "Uranium" },
    { "PrototechScrap", "Prototech Scrap" },
    { "Stone", "Gravel" },
    
    // Components (Vanilla)
    { "Computer", "Computer" },
    { "Construction", "Construction Comp" },
    { "Detector", "Detector Comp" },
    { "Display", "Display" },
    { "Explosives", "Explosives" },
    { "Girder", "Girder" },
    { "GravityGenerator", "Gravity Comp" },
    { "InteriorPlate", "Interior Plate" },
    { "LargeTube", "Large Steel Tube" },
    { "Medical", "Medical Comp." },
    { "MetalGrid", "Metal Grid" },
    { "Motor", "Motor" },
    { "PowerCell", "Power Cell" },
    { "RadioCommunication", "Radio-comm Comp" },
    { "Reactor", "Reactor Comp" },
    { "ShieldComponent", "Field Emitter" },
    { "SmallTube", "Small Steel Tube" },
    { "SolarCell", "Solar Cell" },
    { "SteelPlate", "Steel Plate" },
    { "Superconductor", "Superconductor" },
    { "Thrust", "Thruster Comp" },
    { "ZoneChip", "Zone Chip" },
    { "BulletproofGlass", "Bulletproof Glass" },
    { "Canvas", "Canvas" },
    { "Field_Modulators", "Field Modulator" },
    
    // Modded Components
    { "AdaptiveDynoCapacitor", "Adaptive Dyno Capacitor" },
    { "AryxLynxon_FusionComponent", "Fusion Coil" },
    { "GammaMeshRefractor", "Gamma Mesh Refractor" },
    { "GraphineGrid", "Graphine Grid" },
    { "LeapTube", "Leap Tube" },
    { "PrototechPanel", "Prototech Panel" },
    { "QuantumCoProcessor", "Quantum Co Processor" },
    
    // Ammunition – use friendly names only.
	{ "LargeCalibreAmmo", "Artillery Shell" },
	{ "MediumCalibreAmmo", "Assault Cannon Shell" },
	{ "AutocannonClip", "Autocannon Magazine" },
	{ "Bolter8Inch", "Bolter 8 Inch Shell" },
	{ "C300AmmoAP", "300mm AP Ammo" },
	{ "C300AmmoG", "300mm Guided Ammo" },
	{ "C300AmmoHE", "300mm HE Ammo" },
	{ "C30Ammo", "30mm Standard Ammo" },
	{ "C30DUammo", "30mm DU Ammo" },
	{ "C400AmmoAP", "400mm AP Ammo" },
	{ "C400AmmoCluster", "400mm Cluster Ammo" },
	{ "C400AmmoHE", "400mm HE Ammo" },
	{ "C500AmmoAP", "500mm AP Ammo" },
	{ "C500AmmoCasaba", "500mm Casaba Ammo" },
	{ "C500AmmoHE", "500mm HE Ammo" },
	{ "CRAM30mmAmmo", "30mm C-RAM Ammo" },
	{ "DestroyerMissileMk1", "Destroyer Missile Mk1"},
	{ "DestroyerMissileX", "Destroyer Missile X" },
	{ "H203Ammo", "203mm HE Ammo" },
	{ "H203AmmoAP", "203mm AP Ammo" },
	{ "LargeRailgunAmmo", "Large Railgun Sabot" },
	{ "MA_150mm", "150mm HE Rounds" },
	{ "MA_30mm", "30mm Rounds" },
	{ "MA_Missile", "MA Missiles" },
	{ "Missile200mm", "Rocket" },
	{ "MXA_ArcherPods_Ammo", "M42 Archer Expl" },
	{ "MXA_ArcherPods_KineticAmmo", "M42 Archer Kin" },
	{ "MXA_BreakWater_APAmmo", "520mm AP-Round" },
	{ "MXA_BreakWater_GAmmo", "520mm Guided Round" },
	{ "MXA_BreakWater_HEAmmo", "520mm HE-Round" },
	{ "MXA_Coil155APAmmo", "155mm Coilgun AP" },
	{ "MXA_Coil155HEAmmo", "155mm Coilgun HE" },
	{ "MXA_Coil270Ammo", "270mm Coilgun AP-Round" },
	{ "MXA_Coil270HEAmmo", "270mm Coilgun HE-Round" },
	{ "MXA_Coil305Ammo", "305mm Coilgun AP Round"},
	{ "MXA_Coil305GAmmo", "305mm Coilgun Guided Round" },
	{ "MXA_Coil305HEAmmo", "305mm Coilgun HE Round" },
	{ "MXA_CoilgunPD_Ammo", "50mm Coilgun HE Round" },
	{ "MXA_M58ArcherPods_Ammo", "M58 Archer Expl" },
	{ "MXA_M58ArcherPods_KineticAmmo", "M58 Archer Kin" },
	{ "MXA_MACL_Ammo", "1000mm MAC Round" },
	{ "MXA_MACL_S_Ammo", "600mm MAC Round" },
	{ "MXA_Sabre_Coilgun_Ammo", "30mm Coilgun HE Round" },
	{ "MXA_Shiva_Ammo", "Shiva-Class Nuclear Missile" },
	{ "MXA_SMAC_Ammo", "1500mm MAC Round" },
	{ "NATO_25x184mm", "NATO_25x184mm" },
	{ "NATO_5p56x45mm", "NATO_5p56x45mm" },
	{ "NovaPlasmaInjectorPack", "Nova Turret Plasma Injectors" },
	{ "PaladinCompressor", "Paladin Power Compressor" },
	{ "R150ammo", "150mm Railgun Ammo" },
	{ "R250ammo", "250mm Railgun Ammo" },
	{ "R75ammo", "75mm Railgun Ammo" },
	{ "SmallRailgunAmmo", "SmallRailgunAmmo" },
	{ "SwarmMissile50mm", "SwarmMissile50mm" },
	{ "TorpedoMk1", "Boomer Torpedo Mk1" },
    
    // Currency Icon
    { DEFAULT_CURRENCY_TYPE, "Tech8x" }
};

// Build inverse mapping: friendly → raw.
Dictionary<string, string> friendlyToRaw = new Dictionary<string, string>();
void BuildFriendlyToRawMapping()
{
    friendlyToRaw.Clear();
    foreach (var kvp in itemSubtypeIds)
    {
        if (!friendlyToRaw.ContainsKey(kvp.Value))
            friendlyToRaw[kvp.Value] = kvp.Key;
    }
}
string GetRawKey(string friendlyName)
{
    if (friendlyToRaw.ContainsKey(friendlyName))
        return friendlyToRaw[friendlyName];
    return friendlyName;
}

// --------------------------------------------------
// HELPER: convert a friendly menu name into a MyItemType
// --------------------------------------------------
MyItemType ResolveItemType(string itemName)
{
    // 1) Turn the friendly name into the raw blueprint key
    string rawKey = GetRawKey(itemName);

    // 2) Decide whether it’s ingot, ammo, or component
    bool isIngot = menus[MenuNames.BuyIngots].Contains(itemName)
                || menus[MenuNames.SellIngots].Contains(itemName);
                
    bool isAmmo  = menus[MenuNames.BuyAmmunition].Contains(itemName)
                || menus[MenuNames.SellAmmunition].Contains(itemName);

    // 3) Build the correct MyItemType
    if (isAmmo)
        return MyItemType.Parse("MyObjectBuilder_AmmoMagazine/" + rawKey);
    else if (isIngot)
        return MyItemType.MakeIngot(rawKey);
    else
        return MyItemType.MakeComponent(rawKey);
}

//////////////////////////
// 6) SPRITE / LCD HELPERS 
//    
//////////////////////////

List<string> GenerateExchangeOptions(string itemName)
{
    // 1) Turn the friendly menu‐name (e.g. “Iron”) into the raw key (e.g. “Iron”)
    string rawKey = GetRawKey(itemName);

    // 2) Determine the per‐item price (in Tech8x):
    //    If we’re buying, look up buyPrices; 
    //    if selling, look up sellPrices.
    double price = isBuyMode
        ? buyPrices[rawKey]
        : sellPrices[rawKey];

    // 3) Figure out how many Tech8x we need for 1 item:
    //    If price >= 1, then “1 Tech8x = price-of-1-item” means 1 item costs price Tech.
    //    If price < 1 (fractional), that means “X Tech8x = 1 item” with X = Ceil(1/price).
    int baseTech = (price < 1.0)
        ? (int)Math.Ceiling(1.0 / price)
        : 1;  // If price ≥ 1, then 1 Tech8x buys 1/price items; for convenience, we show "1 → 1/price", etc.

    // 4) Build up a few common multiples: 1×base, 5×base, 10×base, 50×base, 100×base, 500×base, 1000×base.
    List<string> options = new List<string>();
    int[] multiples = new int[] { 1, 5, 10, 50, 100, 500, 1000 };
    foreach (int m in multiples)
    {
        options.Add((baseTech * m).ToString());
    }

    // 5) Finally, “ALL” (spend all you have) and “Back”
    options.Add("ALL");
    options.Add("Back");
    return options;
}


enum Category { Ingot, Component, Ammo, Override }
Dictionary<string,Category> categoryOf = new Dictionary<string,Category>()
{
    // Ingot category
    { "Iron", Category.Ingot },     { "Gold", Category.Ingot },   { "Silver", Category.Ingot },
    { "Nickel", Category.Ingot },   { "Cobalt", Category.Ingot },  { "Platinum", Category.Ingot },
    { "Silicon", Category.Ingot },  { "Magnesium", Category.Ingot }, { "Uranium", Category.Ingot },

    // Component category
    { "Computer", Category.Component },  { "Construction", Category.Component },
    { "Detector", Category.Component },  { "Display", Category.Component },
    { "Explosives", Category.Component },{ "Girder", Category.Component },
    { "GravityGenerator", Category.Component }, { "InteriorPlate", Category.Component },
    { "LargeTube", Category.Component }, { "Medical", Category.Component },
    { "MetalGrid", Category.Component }, { "Motor", Category.Component },
    { "PowerCell", Category.Component }, { "RadioCommunication", Category.Component },
    { "Reactor", Category.Component },   { "ShieldComponent", Category.Component },
    { "SmallTube", Category.Component }, { "SolarCell", Category.Component },
    { "SteelPlate", Category.Component }, { "Superconductor", Category.Component },
    { "Thrust", Category.Component },    { "ZoneChip", Category.Component },
    { "BulletproofGlass", Category.Component },

    // Modded components (if needed)
    { "AdaptiveDynoCapacitor", Category.Override }, // because this is “MyObjectBuilder_Component/…” anyway
    { "AryxLynxon_FusionComponent", Category.Override },
    { "GammaMeshRefractor", Category.Override }, 
    { "GraphineGrid", Category.Override },
    { "LeapTube", Category.Override },
    { "QuantumCoProcessor", Category.Override },

    // Ammo category
    { "AutocannonClip", Category.Ammo },{ "Bolter8Inch", Category.Ammo },
    { "C300AmmoAP", Category.Ammo },   { "C300AmmoG", Category.Ammo },
    { "C300AmmoHE", Category.Ammo },   { "C30Ammo", Category.Ammo },
    { "C30DUammo", Category.Ammo },    { "C400AmmoAP", Category.Ammo },
    { "C400AmmoCluster", Category.Ammo },     { "C400AmmoHE", Category.Ammo },
    { "C500AmmoAP", Category.Ammo },   { "C500AmmoCasaba", Category.Ammo },
    { "C500AmmoHE", Category.Ammo },   { "CRAM30mmAmmo", Category.Ammo },
    { "DestroyerMissileMk1", Category.Ammo }, { "DestroyerMissileX", Category.Ammo },
    { "H203Ammo", Category.Ammo },     { "H203AmmoAP", Category.Ammo },
    { "LargeRailgunAmmo", Category.Ammo }, { "MA_150mm", Category.Ammo },
	{ "LargeCalibreAmmo", Category.Ammo }, { "MediumCalibreAmmo", Category.Ammo },
    { "MA_30mm", Category.Ammo },      { "MA_Missile", Category.Ammo },
    { "Missile200mm", Category.Ammo }, { "MXA_ArcherPods_Ammo", Category.Ammo },
    { "MXA_ArcherPods_KineticAmmo", Category.Ammo }, { "MXA_BreakWater_APAmmo", Category.Ammo },
    { "MXA_BreakWater_GAmmo", Category.Ammo },       { "MXA_BreakWater_HEAmmo", Category.Ammo },
    { "MXA_Coil155HEAmmo", Category.Ammo },          { "MXA_Coil270Ammo", Category.Ammo },
    { "MXA_Coil270HEAmmo", Category.Ammo },          { "MXA_Coil305Ammo", Category.Ammo },
    { "MXA_Coil305GAmmo", Category.Ammo },           { "MXA_Coil305HEAmmo", Category.Ammo },
    { "MXA_CoilgunPD_Ammo", Category.Ammo },         { "MXA_M58ArcherPods_Ammo", Category.Ammo },
    { "MXA_M58ArcherPods_KineticAmmo", Category.Ammo }, { "MXA_MACL_Ammo", Category.Ammo },
    { "MXA_MACL_S_Ammo", Category.Ammo },            { "MXA_Sabre_Coilgun_Ammo", Category.Ammo },
    { "MXA_Shiva_Ammo", Category.Ammo },             { "MXA_SMAC_Ammo", Category.Ammo },
    { "NovaPlasmaInjectorPack", Category.Ammo },     { "PaladinCompressor", Category.Ammo },
    { "R150ammo", Category.Ammo },                   { "R250ammo", Category.Ammo },
    { "R75ammo", Category.Ammo },                    { "SmallRailgunAmmo", Category.Ammo },
    { "SwarmMissile50mm", Category.Ammo },           { "TorpedoMk1", Category.Ammo },

    // Currency
    { DEFAULT_CURRENCY_TYPE, Category.Component }
};

string GetSpriteId(string friendly) {
    if (!categoryOf.ContainsKey(friendly)) return "";
    switch(categoryOf[friendly]) {
        case Category.Ingot:
            return "MyObjectBuilder_Ingot/" + friendly;
        case Category.Component:
            return "MyObjectBuilder_Component/" + friendly;
        case Category.Ammo:
            return "MyObjectBuilder_AmmoMagazine/" + friendly;
        case Category.Override:
            // any oddballs that don't follow the usual pattern
            if (friendly == "PrototechScrap") return "MyObjectBuilder_Ingot/PrototechScrap";
            if (friendly == "Stone")         return "MyObjectBuilder_Ore/Stone";
            // everything else under Override actually follows the standard:
            return "MyObjectBuilder_Component/" + friendly;
    }
    return "";
}

// --------------------------------------------------
// HELPER: add text to a sprite frame
// --------------------------------------------------
void AddText(MySpriteDrawFrame frame, string text, Vector2 pos, float scale, Color c, TextAlignment align) {
    var s = MySprite.CreateText(text, "Monospace", c, scale, align);
    s.Position = pos;
    frame.Add(s);
}

// --------------------------------------------------
// COLLAPSED “RenderScreen” METHOD (replaces ~600+ lines)
// --------------------------------------------------
void RenderScreen(MySpriteDrawFrame frame, Vector2 center) {
    // -----------------------
    // A) Draw black background
    // -----------------------
    var bg = MySprite.CreateSprite("SquareSimple", center, new Vector2(512,512));
    bg.Color = Color.Black;
    frame.Add(bg);

    // -----------------------
    // B) Draw Header / Title
    // -----------------------
    string header; Color hColor;
    if (currentMenu == MenuNames.Welcome) {
        header = "WELCOME TO HAM'S";
        hColor = Color.Orange;
        AddText(frame, header, new Vector2(center.X,70), 1.2f, hColor, TextAlignment.CENTER);
        string techSpr = GetSpriteId(DEFAULT_CURRENCY_TYPE);
        if (!string.IsNullOrEmpty(techSpr)) {
            var icon = MySprite.CreateSprite(techSpr, new Vector2(center.X, center.Y-20), new Vector2(240,240));
            frame.Add(icon);
        }
        AddText(frame, "Press the ", new Vector2(center.X-200, center.Y+120), 1.0f, Color.White, TextAlignment.LEFT);
        AddText(frame, "green",     new Vector2(center.X-14,  center.Y+120), 1.0f, Color.Green, TextAlignment.LEFT);
        AddText(frame, " button",   new Vector2(center.X+70,  center.Y+120), 1.0f, Color.White, TextAlignment.LEFT);
        var check = MySprite.CreateSprite("IconFA-Check", new Vector2(center.X, center.Y+160), new Vector2(64,64));
        check.Color = Color.Lime;
        frame.Add(check);
        return; 
    }
    else if (currentMenu == MenuNames.TradeMode) {
        header = "TRADE MODE"; 
        hColor = new Color(255,165,0);
    }
    else if (currentMenu.StartsWith("Buy")) {
        header = "BUY MODE"; 
        hColor = Color.Green;
    }
    else if (currentMenu.StartsWith("Sell")) {
        header = "SELL MODE"; 
        hColor = Color.Red;
    }
    else {
        header = isBuyMode ? "BUYING" : "SELLING";
        hColor = isBuyMode ? Color.Green : Color.Red;
    }
    AddText(frame, header + " MENU", new Vector2(center.X,30), 1.0f, hColor, TextAlignment.CENTER);

    // ----------------------------------
    // C) Draw ratio line if on sub‐item
    // ----------------------------------
    if (currentMenu == MenuNames.BuyIngots || currentMenu == MenuNames.BuyComponents ||
        currentMenu == MenuNames.BuyModdedComponents || currentMenu == MenuNames.BuyAmmunition ||
        currentMenu == MenuNames.SellIngots || currentMenu == MenuNames.SellComponents ||
        currentMenu == MenuNames.SellModdedComponents || currentMenu == MenuNames.SellAmmunition) 
    {
        string sel = menus[currentMenu][selectedIndex];
        if (!sel.ToLower().Contains("back")) {
            double rate = currentMenu.StartsWith("Buy") 
                ? buyPrices[sel] 
                : sellPrices[sel];
            string ratio = rate >= 1 
                ? $"1 {DEFAULT_CURRENCY_DISPLAY} = {FormatNumber((long)rate)}"
                : $"{(int)Math.Ceiling(1.0/rate)} {DEFAULT_CURRENCY_DISPLAY} = 1";
            string spr = GetSpriteId(DEFAULT_CURRENCY_TYPE);
            if (!string.IsNullOrEmpty(spr)) {
                var icon = MySprite.CreateSprite(spr, new Vector2(center.X - 165,95), new Vector2(48,48));
                frame.Add(icon);
            }
            AddText(frame, ratio, new Vector2(center.X-115,80), 0.7f, Color.White, TextAlignment.LEFT);
        }
    }

    // ---------------------------------------
    // D) Draw special “Exchange” or “Confirm”
    // ---------------------------------------
    if (currentMenu == MenuNames.Exchange) {
        // big icon
        string spr = GetSpriteId(selectedItem);
        if (!string.IsNullOrEmpty(spr)) {
            var bigIcon = MySprite.CreateSprite(spr, new Vector2(center.X+120,260), new Vector2(180,180));
            frame.Add(bigIcon);
        }
        // Name & stock/available lines
        string friendly = selectedItem;
        if (itemSubtypeIds.ContainsKey(selectedItem)) 
            friendly = itemSubtypeIds[selectedItem];
        AddText(frame, friendly, new Vector2(center.X,60), 
                (friendly.Length>12)?1.0f:1.5f, Color.White, TextAlignment.CENTER);
        long stock = isBuyMode 
            ? GetAvailableInStore(selectedItem, true) 
            : GetAvailableInTradeContainer(selectedItem);
        AddText(frame, (isBuyMode?"Stock: ":"Available: ") + FormatNumber(stock),
                new Vector2(center.X,105), 0.6f, Color.White, TextAlignment.CENTER);
        if (!isBuyMode) {
            long storeStock = GetAvailableInStore(selectedItem, true);
            long limit = long.MaxValue;
            if (menus[MenuNames.SellIngots].Contains(selectedItem)) limit = maxIngotLimits[selectedItem];
            else if (menus[MenuNames.SellComponents].Contains(selectedItem)) limit = maxComponentLimits[selectedItem];
            else if (menus[MenuNames.SellModdedComponents].Contains(selectedItem)) limit = maxModdedComponentLimits[selectedItem];
            else if (menus[MenuNames.SellAmmunition].Contains(selectedItem)) limit = maxAmmoLimits[selectedItem];
            AddText(frame, $"Store Stock: {FormatNumber(storeStock)}/{FormatNumber(limit)}", 
                    new Vector2(center.X,130), 0.6f, Color.White, TextAlignment.CENTER);
        }
    }
    else if (currentMenu == MenuNames.Confirmation) {
        // big icon
        string spr = GetSpriteId(selectedItem);
        if (!string.IsNullOrEmpty(spr)) {
            var confIcon = MySprite.CreateSprite(spr, new Vector2(center.X+120,230), new Vector2(180,180));
            frame.Add(confIcon);
        }
        // question text
        string q;
        if (isBuyMode) {
            q = $"Purchase {confirmationMessage}";
        } else {
            double rate = sellPrices[ GetRawKey(selectedItem) ];
            long needed = (long)(pendingTechAmount * rate);
            q = $"Sell {FormatNumber(needed)} {selectedItem} for {pendingTechAmount} {DEFAULT_CURRENCY_DISPLAY}?";
        }
        if (q.Length > 30) {
            int mid = q.Length / 2;
            int sp = q.IndexOf(" ", mid);
            if (sp < 0) sp = mid;
            string l1 = q.Substring(0, sp).Trim();
            string l2 = q.Substring(sp).Trim();
            AddText(frame, l1, new Vector2(center.X,80), 0.6f, Color.Gray, TextAlignment.CENTER);
            AddText(frame, l2, new Vector2(center.X,100), 0.6f, Color.Gray, TextAlignment.CENTER);
        } else {
            AddText(frame, q, new Vector2(center.X,80), 0.6f, Color.Gray, TextAlignment.CENTER);
        }
    }
    else if (currentMenu == MenuNames.InsufficientTech || currentMenu == MenuNames.InsufficientStock) {
        AddText(frame, "ERROR: Insufficient resources or tech!", new Vector2(center.X,100), TEXT_SCALE, Color.Red, TextAlignment.CENTER);
    }

    // ---------------------------------------
    // E) Render the scrollable list of menu items
    // ---------------------------------------
    float startY = (currentMenu == MenuNames.Exchange) ? 150f 
                 : ((currentMenu == MenuNames.Confirmation) ? 140f : 130f);
    Vector2 pos = new Vector2(80, startY);
    List<string> opts = (currentMenu == MenuNames.Exchange)
        ? GenerateExchangeOptions(selectedItem)
        : menus[currentMenu];
    int total = opts.Count;
    int end   = Math.Min(firstVisibleIndex + VISIBLE_ITEM_COUNT, total);

    if (firstVisibleIndex > 0) {
        AddText(frame, "↑", new Vector2(center.X, pos.Y - SCROLL_INDICATOR_OFFSET), TEXT_SCALE, Color.White, TextAlignment.CENTER);
    }

    for (int i = firstVisibleIndex; i < end; i++) {
        string mi = opts[i];
        bool sel = (i == selectedIndex);

        // Lookup friendly name if available
        string displayName = mi;
        if (itemSubtypeIds.ContainsKey(mi)) {
            displayName = itemSubtypeIds[mi];
        }

        string dText = displayName;
        Color  cText = Color.White;

        if (currentMenu == MenuNames.TradeMode) {
            dText = sel ? $"> {displayName} <" : displayName;
            cText = sel ? Color.Yellow : Color.White;
        }
        else if (currentMenu == MenuNames.BuyMode || currentMenu == MenuNames.SellMode) {
            dText = sel ? $"> {displayName} <" : displayName;
            cText = sel ? (isBuyMode ? Color.Green : Color.Red) : Color.White;
        }
        else if (currentMenu == MenuNames.BuyIngots || currentMenu == MenuNames.BuyComponents ||
                 currentMenu == MenuNames.BuyModdedComponents || currentMenu == MenuNames.BuyAmmunition ||
                 currentMenu == MenuNames.SellIngots || currentMenu == MenuNames.SellComponents ||
                 currentMenu == MenuNames.SellModdedComponents || currentMenu == MenuNames.SellAmmunition)
        {
            if (mi.ToLower().Contains("back")) {
                dText = sel ? $"> {displayName} <" : displayName;
                cText = sel ? Color.Yellow : Color.White;
            }
            else {
                long stock = currentMenu.StartsWith("Sell")
                            ? GetAvailableInTradeContainer(mi)
                            : GetAvailableInStore(mi, true);
                bool outOf = (stock <= 0);
                if (sel) {
                    dText = outOf 
                        ? $"> {displayName} [OUT] <"
                        : $"> {displayName} [{FormatNumber(stock)}] <";
                    cText = Color.Yellow;
                } else {
                    dText = outOf 
                        ? $"{displayName} [OUT]" 
                        : $"{displayName} [{FormatNumber(stock)}]";
                    cText = outOf ? Color.Red : Color.White;
                }
            }
            // icon on the left
            if (!mi.ToLower().Contains("back")) {
                string spriteId = GetSpriteId(mi);
                if (!string.IsNullOrEmpty(spriteId)) {
                    var icon = MySprite.CreateSprite(spriteId, new Vector2(60,pos.Y+25), new Vector2(ICON_SIZE,ICON_SIZE)*BASE_SCALE);
                    frame.Add(icon);
                }
            }
        }
        else if (currentMenu == MenuNames.Exchange) {
            if (mi == "Back") {
                dText = sel ? $"> Back <" : "Back";
                cText = sel ? Color.Yellow : Color.White;
            }
            else if (mi == "ALL") {
                dText = sel ? $"> ALL <" : "ALL";
                bool dis = isBuyMode 
                    ? (GetTech8xInTradeContainer(false)<=0 || GetAvailableInStore(selectedItem,true)<=0)
                    : (GetAvailableInTradeContainer(selectedItem)<=0);
                cText = dis ? Color.Gray : (sel?Color.Yellow:Color.White);

                // currency icon on left
                string sp = GetSpriteId(DEFAULT_CURRENCY_TYPE);
                if (!string.IsNullOrEmpty(sp)) {
                    var icon = MySprite.CreateSprite(sp, new Vector2(60,pos.Y+25), new Vector2(ICON_SIZE,ICON_SIZE)*BASE_SCALE);
                    frame.Add(icon);
                }
            }
            else {
                int techAmt = int.Parse(mi);
                double rate = isBuyMode? buyPrices[selectedItem] : sellPrices[selectedItem];
                long needed = (long)Math.Floor(rate * techAmt);
                bool dis = isBuyMode
                    ? (GetTech8xInTradeContainer(false)<techAmt || GetAvailableInStore(selectedItem,true)<needed)
                    : (GetAvailableInTradeContainer(selectedItem)<needed);
                dText = sel 
                    ? $"> {mi} → {FormatNumber(needed)} <" 
                    : $"{mi} → {FormatNumber(needed)}";
                cText = dis? Color.Gray : (sel?Color.Yellow:Color.White);

                // currency icon on left
                string sp = GetSpriteId(DEFAULT_CURRENCY_TYPE);
                if (!string.IsNullOrEmpty(sp)) {
                    var icon = MySprite.CreateSprite(sp, new Vector2(60,pos.Y+25), new Vector2(ICON_SIZE,ICON_SIZE)*BASE_SCALE);
                    frame.Add(icon);
                }
            }
        }
        else if (currentMenu == MenuNames.Confirmation) {
            if (mi == "Yes") {
                dText = sel? "> YES <" : "Yes";
                cText  = sel? Color.Yellow : Color.Gray;
            } else {
                dText = sel? "> NO <" : "No";
                cText  = sel? Color.Yellow : Color.Gray;
            }
        }
        else if (currentMenu == MenuNames.InsufficientTech || currentMenu == MenuNames.InsufficientStock) {
            dText = sel? $"> {displayName} <" : displayName;
            cText = sel? Color.Yellow : Color.White;
        }

        // finally draw the text line
        AddText(frame, dText, new Vector2(120, pos.Y+15), TEXT_SCALE, cText, TextAlignment.LEFT);
        pos.Y += ITEM_SPACING * BASE_SCALE;
    }

    if (end < total) {
        AddText(frame, "↓", new Vector2(center.X, pos.Y + SCROLL_INDICATOR_OFFSET), TEXT_SCALE, Color.White, TextAlignment.CENTER);
    }

    // ------------------------------------------
    // F) Draw footer: small “Tech8x” + amount
    // ------------------------------------------
    {
        float margin = 16f, iconSize=48f;
        float x = center.X*2 - margin - iconSize, y = margin;
        string techSpr = GetSpriteId(DEFAULT_CURRENCY_TYPE);
        if (!string.IsNullOrEmpty(techSpr)) {
            var icon = MySprite.CreateSprite(techSpr, new Vector2(x+iconSize/2,y+iconSize/2), new Vector2(iconSize,iconSize));
            frame.Add(icon);
        }
        AddText(frame, FormatNumber(GetTech8xInTradeContainer(false)), new Vector2(x+iconSize/2,y+iconSize/2), 0.8f, Color.Gold, TextAlignment.CENTER);
    }
}


// --------------------------------------------------
// ALWAYS call this in UpdateLCD()
// --------------------------------------------------
void UpdateLCD() {
    var frame = lcdCustomerScreen.DrawFrame();
    var center = new Vector2(256,256);
    try {
        RenderScreen(frame, center);
    } finally {
        frame.Dispose();
    }
}

/// <summary>
/// Formats a number with K/M/B suffixes; if decimals>0 and num<1, shows up to that many decimals.
/// </summary>
string FormatNumber(double num, int decimals)
{
    // small-number case
    if (num < 1.0 && decimals > 0)
        return num.ToString("0." + new string('#', decimals));

    // large-number tiers
    double[] thresholds = { 1e9, 1e6, 1e3 };
    char[]   suffixes   = { 'B',  'M',  'K'  };
    for (int i = 0; i < thresholds.Length; i++)
    {
        if (num >= thresholds[i])
            return (num / thresholds[i]).ToString("0.#") + suffixes[i];
    }

    // integer case
    return num.ToString(decimals > 0 ? "0." + new string('#', decimals) : "0");
}
/// <summary>
/// Integer-based formatting: K/M/B (used for tables and text panels).
/// </summary>
string FormatNumber(long number)
{
    if (number >= 1000000000L)
        return (number / 1000000000.0).ToString("0.##") + "B";
    if (number >= 1000000L)
        return (number / 1000000.0).ToString("0.##") + "M";
    if (number >= 1000L)
        return (number / 1000.0).ToString("0.##") + "K";
    return number.ToString();
}

void RenderPriceTable(
    IMyTextPanel panel,
    string title,
    IEnumerable<string> allKeys,
    Func<string,bool>   filter,
    Func<string,double> getBuy,
    Func<string,double> getSell,
    Func<string,long>   getStock,
    Func<string,long>   getLimit,
    Func<long,string>   fmtWhole,
    Func<double,int,string> fmtFrac,
    bool paged,
    ref int pageIndex,
    ref bool downwards,
    int pageSize
)
{
    if (panel == null) return;
    var items = allKeys.Where(filter).OrderBy(k=>k).ToList();
    int total  = items.Count;
    int start  = 0, end = total;

    if (paged && pageSize > 0) {
        int totalPages = Math.Max(1, (total + pageSize -1)/pageSize);
        if (downwards) { pageIndex++; if (pageIndex >= totalPages-1) downwards = false; }
        else         { pageIndex--; if (pageIndex <= 0)            downwards = true;  }
        start = pageIndex * pageSize;
        end   = Math.Min(start + pageSize, total);
    }

    var sb = new StringBuilder();
    sb.AppendLine("== " + title + " Prices ==" + (paged ? $"  Page {pageIndex+1}/{Math.Max(1,(total+pageSize-1)/pageSize)}" : ""));
    sb.AppendLine("Item              Buy  Sell    Stock      ");  
    sb.AppendLine("------------------------------------------");

    for (int i = start; i < end; i++) {
        string key  = items[i];
        string raw  = itemSubtypeIds.ContainsKey(key) ? itemSubtypeIds[key] : key;
        string name = raw.Length>15 ? raw.Substring(0,15) : raw;

        double b    = getBuy(key);
        double s    = getSell(key);
        long   a    = getStock(key);
        long   l    = getLimit(key);

        sb.AppendFormat("{0,-16}{1,5}{2,6}  {3,5}\n",
						name,
						fmtFrac(b,2),
						fmtFrac(s,2),
						fmtWhole(a) + "/" + fmtWhole(l));
    }

    panel.ContentType = ContentType.TEXT_AND_IMAGE;
    panel.Font        = "Monospace";
    panel.FontSize    = 0.6f;
    panel.Alignment   = TextAlignment.LEFT;
    panel.WriteText(sb.ToString());
}


void UpdatePriceDisplays()
{
    // Ingots (no paging needed, so pass dummyPageIdx/dummyDown)
    RenderPriceTable(
        lcdIngotPrices, "Ingots",
        menus[MenuNames.BuyIngots].Where(k => !k.StartsWith("Back")),
        k => sellPrices.ContainsKey(k) && maxIngotLimits.ContainsKey(k),
        k => buyPrices[k], k => sellPrices[k],
        k => cachedStoreItems.ContainsKey(k) ? cachedStoreItems[k] : 0,
        k => maxIngotLimits[k],
        v => FormatNumber((long)v), (nv, dc) => FormatNumber(nv, dc),
        false,                // paged?
        ref dummyPageIdx,     // dummy index
        ref dummyDown,        // dummy down/up flag
        0                     // pageSize = 0 => “no paging”
    );

    // Components (no paging)
    RenderPriceTable(
        lcdComponentPrices, "Components",
        menus[MenuNames.BuyComponents].Where(k => !k.StartsWith("Back")),
        k => sellPrices.ContainsKey(k) && maxComponentLimits.ContainsKey(k),
        k => buyPrices[k], k => sellPrices[k],
        k => cachedStoreItems.ContainsKey(k) ? cachedStoreItems[k] : 0,
        k => maxComponentLimits[k],
        v => FormatNumber((long)v), (nv, dc) => FormatNumber(nv, dc),
        false,
        ref dummyPageIdx,
        ref dummyDown,
        0
    );

    // Modded Components (no paging)
    RenderPriceTable(
        lcdModdedComponentPrices, "Modded Components",
        menus[MenuNames.BuyModdedComponents].Where(k => !k.StartsWith("Back")),
        k => sellPrices.ContainsKey(k) && maxModdedComponentLimits.ContainsKey(k),
        k => buyPrices[k], k => sellPrices[k],
        k => cachedStoreItems.ContainsKey(k) ? cachedStoreItems[k] : 0,
        k => maxModdedComponentLimits[k],
        v => FormatNumber((long)v), (nv, dc) => FormatNumber(nv, dc),
        false,
        ref dummyPageIdx,
        ref dummyDown,
        0
    );

    // Ammunition (paged: use real ammoPageIndex and ammoScrollDown)
    RenderPriceTable(
        lcdAmmoPrices, "Ammo",
        menus[MenuNames.BuyAmmunition].Where(k => !k.StartsWith("Back")).ToList(),
        k => sellPrices.ContainsKey(k) && maxAmmoLimits.ContainsKey(k),
        k => buyPrices[k], k => sellPrices[k],
        k => cachedStoreItems.ContainsKey(k) ? cachedStoreItems[k] : 0,
        k => maxAmmoLimits[k],
        v => FormatNumber((long)v), (nv, dc) => FormatNumber(nv, dc),
        true,                // paged
        ref ammoPageIndex,   // real page index for ammo
        ref ammoScrollDown,  // real up/down flag for ammo
        visibleAmmoLines     // pageSize 
    );
}


void UpdateTradePreview(IMyTextPanel panel, IMyCargoContainer container)
{
    var sb = new StringBuilder();
    sb.AppendLine("== Trade Container ==");
    sb.AppendLine("Item                Amount");
    sb.AppendLine("---------------------------");

    if (panel == null || container == null)
    {
        sb.AppendLine("<< Block Missing >>");
        panel?.WriteText(sb.ToString());
        return;
    }

    var inv   = container.GetInventory();
    var items = new List<MyInventoryItem>();
    inv.GetItems(items);

    // sum up by subtype
    var totals = new Dictionary<string, double>();
    foreach (var it in items)
    {
        var sub = it.Type.SubtypeId;
        if (!totals.ContainsKey(sub)) totals[sub] = 0;
        totals[sub] += (double)it.Amount;
    }

    if (totals.Count == 0)
    {
        sb.AppendLine("<< Empty >>");
    }
    else
    {
        foreach (var kv in totals.OrderByDescending(x => x.Value))
        {
            if (kv.Value < 1) continue; // skip fractional dust
            string nice = itemSubtypeIds.ContainsKey(kv.Key) ? itemSubtypeIds[kv.Key] : kv.Key;
            string name = nice.Length > 20 ? nice.Substring(0, 20) : nice;
            string amt  = FormatNumber(kv.Value, 0);
            sb.AppendFormat("{0,-20}{1,8}\n", name, amt);
        }
    }

    // current / max volume of trade container (in L)
    double curVol = inv.CurrentVolume.RawValue / 1000.0;
    double maxVol = inv.MaxVolume.RawValue    / 1000.0;
    double pct    = maxVol > 0 ? (curVol / maxVol * 100.0) : 0.0;

    sb.AppendLine();
    sb.AppendLine($"Volume: {FormatNumber(curVol,0)} / {FormatNumber(maxVol,0)} L");
    sb.AppendLine($"({pct:0.#}%)");

    // Tier 3 container capacity (L)
    const double Tier3Capacity = 33750000.0;

    // how many containers needed (always ceil)
    long containersNeeded = (long)Math.Ceiling(curVol / Tier3Capacity);

    // determine fullness of the last container
    double lastPct;
    if (containersNeeded <= 0)
    {
        // empty case: no containers needed, 0% full
        containersNeeded = 0;
        lastPct = 0.0;
    }
    else if (containersNeeded == 1)
    {
        // single container: fullness = entire volume / capacity
        lastPct = (curVol / Tier3Capacity) * 100.0;
    }
    else
    {
        // more than one: fullness of the final one
        double lastVol = curVol - (containersNeeded - 1) * Tier3Capacity;
        lastPct = (lastVol / Tier3Capacity) * 100.0;
    }

    sb.AppendLine($"Containers needed (T3): {containersNeeded} ({lastPct:0.#}% full)");

    // write out
    panel.ContentType = ContentType.TEXT_AND_IMAGE;
    panel.Font        = "Monospace";
    panel.FontSize    = 0.6f;
    panel.Alignment   = TextAlignment.CENTER;
    panel.WriteText(sb.ToString());
}



void UpdateAdminLCD()
{
    if(adminLCD == null) return;
    
    // Retrieve the persistent log from CustomData.
    string persistentLog = adminLCD.CustomData;
    
    // Combine admin info and persistent log for display.
    string displayText = "\n--- Transaction Log ---\n" + persistentLog;
    
    // Write the combined text to the admin LCD's visible text.
    adminLCD.WriteText(displayText, false);
}

//////////////////////////////////////
// 7) STATE MACHINE ENUMS AND TRACKERS 
//////////////////////////////////////

enum PurchaseState { Idle, Phase0_Staging, Phase1_Final }
enum SaleState     { Idle, Phase0_Staging, Phase1_Final }

PurchaseState _purchaseState = PurchaseState.Idle;
SaleState     _saleState     = SaleState.Idle;

// Temporary holders for the parameters the state machines use:
string _pm_ItemName;    long _pm_ItemQty;    int _pm_TechAmount; // Purchase
string _sm_ItemName;    long _sm_ItemQty;    int _sm_TechAmount; // Sale

/////////////////////////////////////////////
// 8) PROGRAM CONSTRUCTOR: INITIALIZE BLOCKS 
/////////////////////////////////////////////

public Program()
{
    // 1) Load LCDs, ButtonPanel, TradeContainer, TechVault, Price LCDs, TradePreview
    lcdCustomerScreen   = GridTerminalSystem.GetBlockWithName("LCD_Customer") as IMyTextPanel;
    debugLCD            = GridTerminalSystem.GetBlockWithName("LCD_Debug")    as IMyTextPanel;
    adminLCD            = GridTerminalSystem.GetBlockWithName("LCD_Admin")    as IMyTextPanel;
    buttonPanel         = GridTerminalSystem.GetBlockWithName("ButtonPanel_Trade") as IMyButtonPanel;
    TradeContainer      = GridTerminalSystem.GetBlockWithName("TradeContainer [Locked]") as IMyCargoContainer;
    techVault           = GridTerminalSystem.GetBlockWithName("ExoVAULT [Locked]")    as IMyCargoContainer;
    lcdIngotPrices      = GridTerminalSystem.GetBlockWithName("LCD Ingot Prices") as IMyTextPanel;
    lcdComponentPrices  = GridTerminalSystem.GetBlockWithName("LCD Component Prices") as IMyTextPanel;
    lcdModdedComponentPrices = GridTerminalSystem.GetBlockWithName("LCD Modded Component Prices") as IMyTextPanel;
    lcdAmmoPrices       = GridTerminalSystem.GetBlockWithName("LCD Ammo Prices") as IMyTextPanel;
    lcdTradePreview     = GridTerminalSystem.GetBlockWithName("LCD Trade Preview") as IMyTextPanel;
	customerConnector = GridTerminalSystem.GetBlockWithName("Connector [Back] [No IIM]") as IMyShipConnector;

    // 2) Load all store containers in group “STORE INVENTORY”
    IMyBlockGroup storeGroup = GridTerminalSystem.GetBlockGroupWithName("STORE INVENTORY");
    if (storeGroup != null)
    {
        storeGroup.GetBlocksOfType(storeContainers);
        // Filter out TradeContainer and techVault
        var filtered = new List<IMyCargoContainer>();
        foreach (var c in storeContainers)
            if (c.CubeGrid == Me.CubeGrid && c != TradeContainer && c != techVault)
                filtered.Add(c);
        storeContainers = filtered;
    }
    else
    {
        Echo("Error: STORE INVENTORY group not found.");
        throw new Exception("Initialization failed: STORE INVENTORY group missing");
    }

    // 3) Verify required blocks (LCDs, containers, button panel)
    CheckBlocks();

    // 4) Initialize Price mappings, friendly/raw mapping, and initial cache
    BuildFriendlyToRawMapping();
    SetCustomPricing();
    cachedTechAmount = GetTech8xInTradeContainer(false);

    // 5) Initialize sorter & holding‐container references
    _sorterA = GridTerminalSystem.GetBlockWithName("Sorter StorageToHoldingA")  as IMyConveyorSorter;
    _sorterB = GridTerminalSystem.GetBlockWithName("Sorter HoldingAToTrade")     as IMyConveyorSorter;
    _sorterC = GridTerminalSystem.GetBlockWithName("Sorter TradeToHoldingB")     as IMyConveyorSorter;
    _sorterD = GridTerminalSystem.GetBlockWithName("Sorter HoldingBToStorage") as IMyConveyorSorter;
    if (_sorterA == null) throw new Exception("Missing: Sorter StorageToHoldingA");
    if (_sorterB == null) throw new Exception("Missing: Sorter HoldingAToTrade");
    if (_sorterC == null) throw new Exception("Missing: Sorter TradeToHoldingB");
    if (_sorterD == null) throw new Exception("Missing: Sorter HoldingBToVaultOrStore");

    _holdingA = GridTerminalSystem.GetBlockWithName("Holding A [Locked]") as IMyCargoContainer;
    _holdingB = GridTerminalSystem.GetBlockWithName("Holding B [Locked]") as IMyCargoContainer;
    if (_holdingA == null) throw new Exception("Missing: Holding A");
    if (_holdingB == null) throw new Exception("Missing: Holding B");

    // 6) Initial LCD draw
    UpdateLCD();
    UpdateAdminLCD();

    // 7) Schedule script to run every 10 ticks (so state machines process)
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

// Block‐present checks (unchanged)
void CheckBlocks()
{
    if (lcdCustomerScreen == null) throw new Exception("Missing LCD_Customer");
    if (buttonPanel       == null) throw new Exception("Missing ButtonPanel_Trade");
    if (TradeContainer    == null) throw new Exception("Missing TradeContainer [Locked]");
    if (techVault         == null) throw new Exception("Missing TECHVAULT [Locked]");
}

////////////////////////////////////////////////////////////////
// 9) UTILITY METHODS (Log, LogTransaction, inventory lookups, etc.)
////////////////////////////////////////////////////////////////

void Log(string message, bool appendToLCD = true)
{
    if (debugMode)
    {
        string context = "[Menu: " + currentMenu + "]";
        Echo(context + " " + message);
        if (debugLCD != null && appendToLCD)
            debugLCD.WriteText(context + " " + message + "\n", true);
    }
}

void LogTransaction(string message)
{
    // 1) timestamp
    string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    // 2) trader info
    string grid    = GetTraderGridName();
    long   ownerId = GetTraderOwnerId();

    // 3) compose the entry
    string entry = $"[{ts}] [Trade with {grid} (Owner {ownerId})]: {message}";

    // 4) append to your adminLCD
    if (adminLCD != null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(adminLCD.CustomData))
            sb.AppendLine(adminLCD.CustomData.TrimEnd('\n'));
        sb.AppendLine(entry);
        adminLCD.CustomData = sb.ToString();
        adminLCD.WriteText(adminLCD.CustomData, false);
    }
}
string GetTraderGridName()
{
    if (customerConnector != null
        && customerConnector.Status == MyShipConnectorStatus.Connected)
    {
        var other = customerConnector.OtherConnector;
        if (other != null)
            return other.CubeGrid.CustomName;
    }
    return "Unknown";
}

long GetTraderOwnerId()
{
    if (customerConnector != null
        && customerConnector.Status == MyShipConnectorStatus.Connected)
    {
        var other = customerConnector.OtherConnector;
        if (other != null)
            return other.OwnerId;
    }
    return 0L;
}

int GetTech8xInTradeContainer(bool useCache)
{
    if (useCache) return cachedTechAmount;
    var item = MyItemType.MakeComponent(DEFAULT_CURRENCY_TYPE);
    return (int)TradeContainer.GetInventory().GetItemAmount(item);
}

long GetAvailableInStore(string itemType, bool useCache)
{
    string rawKey = GetRawKey(itemType);
    if (useCache && cachedStoreItems.ContainsKey(itemType))
        return cachedStoreItems[itemType];

    bool isIngot = menus[MenuNames.BuyIngots].Contains(itemType) || menus[MenuNames.SellIngots].Contains(itemType);
    bool isAmmo  = menus[MenuNames.BuyAmmunition].Contains(itemType) || menus[MenuNames.SellAmmunition].Contains(itemType);
    MyItemType mt;
    if (isAmmo)
        mt = MyItemType.Parse("MyObjectBuilder_AmmoMagazine/" + rawKey);
    else if (isIngot)
        mt = MyItemType.MakeIngot(rawKey);
    else
        mt = MyItemType.MakeComponent(rawKey);

    long total = 0;
    foreach (var container in storeContainers)
        total += (long)container.GetInventory().GetItemAmount(mt);
    return total;
}

long GetAvailableInTradeContainer(string itemType)
{
    string rawKey = GetRawKey(itemType);
    bool isIngot = menus[MenuNames.BuyIngots].Contains(itemType) || menus[MenuNames.SellIngots].Contains(itemType);
    bool isAmmo  = menus[MenuNames.BuyAmmunition].Contains(itemType) || menus[MenuNames.SellAmmunition].Contains(itemType);
    MyItemType mt;
    if (isAmmo)
        mt = MyItemType.Parse("MyObjectBuilder_AmmoMagazine/" + rawKey);
    else if (isIngot)
        mt = MyItemType.MakeIngot(rawKey);
    else
        mt = MyItemType.MakeComponent(rawKey);

    return (long)TradeContainer.GetInventory().GetItemAmount(mt);
}

void CacheInventoryData()
{
    cachedStoreItems.Clear();
    foreach (var key in menus[MenuNames.BuyIngots])
        if (!key.Contains("Back")) cachedStoreItems[key] = GetAvailableInStore(key, false);
    foreach (var key in menus[MenuNames.BuyComponents])
        if (!key.Contains("Back")) cachedStoreItems[key] = GetAvailableInStore(key, false);
    foreach (var key in menus[MenuNames.BuyModdedComponents])
        if (!key.Contains("Back")) cachedStoreItems[key] = GetAvailableInStore(key, false);
    foreach (var key in menus[MenuNames.BuyAmmunition])
        if (!key.Contains("Back")) cachedStoreItems[key] = GetAvailableInStore(key, false);

    cachedTechAmount = GetTech8xInTradeContainer(false);
}

////////////////////////////////////////////////////////////////
// 10) NAVIGATION: HandleSelect, Navigate, GoBack (unchanged)
////////////////////////////////////////////////////////////////

void Navigate(int direction)
{
    List<string> options = (currentMenu == MenuNames.Exchange)
        ? GenerateExchangeOptions(selectedItem)
        : menus[currentMenu];
    int newIndex = MathHelper.Clamp(selectedIndex + direction, 0, options.Count - 1);
    if(newIndex != selectedIndex)
    {
        if(newIndex >= firstVisibleIndex + VISIBLE_ITEM_COUNT)
            firstVisibleIndex = newIndex - VISIBLE_ITEM_COUNT + 1;
        else if(newIndex < firstVisibleIndex)
            firstVisibleIndex = newIndex;
        selectedIndex = newIndex;
    }
    UpdateLCD();
}

void NavigateTo(string menu)
{
    menuStack.Push(currentMenu);
    currentMenu = menu;
    // If navigating to Confirmation, default to "No" (index 1); otherwise, default to 0.
    selectedIndex = (menu == MenuNames.Confirmation) ? 1 : 0;
    firstVisibleIndex = 0;
    Log("Entered menu: " + menu);
    UpdateLCD();
}

void GoBack()
{
    if (menuStack.Count > 0)
    {
        // Remember where we’re coming from
        string fromMenu = currentMenu;
        // Pop back to the previous menu
        string prevMenu = menuStack.Pop();
        currentMenu = prevMenu;

        // If we just left the Exchange screen, restore the last indices:
        if (fromMenu == MenuNames.Exchange)
        {
            selectedIndex     = savedExchangeIndex;
            firstVisibleIndex = savedExchangeScroll;
        }
        else
        {
            // otherwise reset to the top
            selectedIndex     = 0;
            firstVisibleIndex = 0;
        }

        Log("Returned to: " + currentMenu);
        UpdateLCD();
    }
    else
    {
        // Already at root, go back to TradeMode
        currentMenu       = MenuNames.TradeMode;
        selectedIndex     = 0;
        firstVisibleIndex = 0;
        Log("Already at root menu, returning to TradeMode");
        UpdateLCD();
    }
}

void HandleSelect()
{
    // Welcome → TradeMode
    if (currentMenu == MenuNames.Welcome)
    {
        currentMenu = MenuNames.TradeMode;
        selectedIndex = 0;
        firstVisibleIndex = 0;
        UpdateLCD();
        return;
    }

    // TradeMode → BuyMode / SellMode
    if (currentMenu == MenuNames.TradeMode)
    {
        string choice = menus[MenuNames.TradeMode][selectedIndex];
        isBuyMode = (choice == "Buy");
        currentMenu = isBuyMode ? MenuNames.BuyMode : MenuNames.SellMode;
        selectedIndex = 0;
        firstVisibleIndex = 0;
        UpdateLCD();
        return;
    }

    // BuyMode / SellMode → category submenu
    if (currentMenu == MenuNames.BuyMode || currentMenu == MenuNames.SellMode)
    {
        string category = menus[currentMenu][selectedIndex];
        NavigateTo(category);
        return;
    }

    // category submenu → Exchange
    if (   currentMenu == MenuNames.BuyIngots   || currentMenu == MenuNames.BuyComponents
        || currentMenu == MenuNames.BuyModdedComponents || currentMenu == MenuNames.BuyAmmunition
        || currentMenu == MenuNames.SellIngots  || currentMenu == MenuNames.SellComponents
        || currentMenu == MenuNames.SellModdedComponents || currentMenu == MenuNames.SellAmmunition)
    {
        string selOpt = menus[currentMenu][selectedIndex];
        if (selOpt.StartsWith("Back"))
        {
            GoBack();
            return;
        }
        selectedItem = selOpt;

        savedExchangeIndex  = selectedIndex;
        savedExchangeScroll = firstVisibleIndex;

        NavigateTo(MenuNames.Exchange);
        return;
    }

    // Exchange → Confirmation (or back), with grey‑out inert handling
    if (currentMenu == MenuNames.Exchange)
    {
        List<string> opts = GenerateExchangeOptions(selectedItem);
        string selOpt = opts[selectedIndex];

        // Back always works
        if (selOpt == "Back")
        {
            GoBack();
            return;
        }

        // --- SHORT‑CIRCUIT GREYED‑OUT (OVER‑LIMIT) SELL OPTIONS ---
        if (!isBuyMode)
        {
            long storeHas = GetAvailableInStore(selectedItem, true);
            long limit = long.MaxValue;
            if (menus[MenuNames.SellIngots].Contains(selectedItem) && maxIngotLimits.ContainsKey(selectedItem))
            {
                limit = maxIngotLimits[selectedItem];
            }
            else if (menus[MenuNames.SellComponents].Contains(selectedItem) && maxComponentLimits.ContainsKey(selectedItem))
            {
                limit = maxComponentLimits[selectedItem];
            }
            else if (menus[MenuNames.SellModdedComponents].Contains(selectedItem) && maxModdedComponentLimits.ContainsKey(selectedItem))
            {
                limit = maxModdedComponentLimits[selectedItem];
            }
            else if (menus[MenuNames.SellAmmunition].Contains(selectedItem) && maxAmmoLimits.ContainsKey(selectedItem))
            {
                limit = maxAmmoLimits[selectedItem];
            }
            long spaceLeft = limit - storeHas;

            bool isDisabled = false;
            if (selOpt == "ALL")
            {
                long tradeStock = GetAvailableInTradeContainer(selectedItem);
                isDisabled = (tradeStock <= 0 || spaceLeft <= 0);
            }
            else
            {
                int parsedAmt;
                if (int.TryParse(selOpt, out parsedAmt))
                {
                    long needed = (long)Math.Floor(sellPrices[selectedItem] * parsedAmt);
                    long tradeStock = GetAvailableInTradeContainer(selectedItem);
                    isDisabled = (tradeStock < needed || needed > spaceLeft);
                }
            }

            if (isDisabled)
                return;  // ignore greyed‑out entries
        }

        // --- EXISTING EXCHANGE LOGIC ---
        int techAmount;
        long itemsToTransfer;
        double rate = isBuyMode
            ? buyPrices[selectedItem]
            : sellPrices[selectedItem];

        if (selOpt == "ALL")
        {
            if (isBuyMode)
            {
                // 1) Tech deposited
                int deposit = GetTech8xInTradeContainer(false);
                // 2) Station stock
                long storeHas = GetAvailableInStore(selectedItem, true);
                // 3) Reserve one Tech’s worth
                long reserved = (long)Math.Floor(rate * 1);
                long maxXfer = Math.Max(0, storeHas - reserved);
                // 4) Affordability
                long wanted = (long)Math.Floor(rate * deposit);
                itemsToTransfer = Math.Min(wanted, maxXfer);
                if (itemsToTransfer < 1)
                {
                    NavigateTo(MenuNames.InsufficientStock);
                    return;
                }
                // 5) Tech spent
                techAmount = (int)Math.Ceiling(itemsToTransfer / rate);
            }
            else
            {
                // Sell-all
                List<MyInventoryItem> list = new List<MyInventoryItem>();
                TradeContainer.GetInventory().GetItems(list);
                bool isIngot = menus[MenuNames.SellIngots].Contains(selectedItem);
                MyItemType mt = isIngot
                    ? MyItemType.MakeIngot(selectedItem)
                    : MyItemType.MakeComponent(selectedItem);
                long totalItems = 0;
                foreach (MyInventoryItem item in list)
                {
                    if (item.Type == mt)
                        totalItems += (long)item.Amount;
                }
                techAmount = (int)Math.Floor(totalItems / rate);
                if (techAmount < 1)
                {
                    NavigateTo(MenuNames.InsufficientStock);
                    return;
                }
                itemsToTransfer = (long)(rate * techAmount);
            }
        }
        else
        {
            // parse the tech the user wants to spend
            techAmount = int.Parse(selOpt);
            // compute how many items that would buy/sell
            itemsToTransfer = (long)Math.Floor(rate * techAmount);

            // ===== PRE‑CONFIRM CHECKS =====
            if (isBuyMode)
            {
                // not enough Tech deposited?
                if (GetTech8xInTradeContainer(false) < techAmount)
                {
                    NavigateTo(MenuNames.InsufficientTech);
                    return;
                }
                // not enough stock in the store?
                if (GetAvailableInStore(selectedItem, true) < itemsToTransfer)
                {
                    NavigateTo(MenuNames.InsufficientStock);
                    return;
                }
            }
            else
            {
                // not enough item deposited to sell?
                if (GetAvailableInTradeContainer(selectedItem) < itemsToTransfer)
                {
                    NavigateTo(MenuNames.InsufficientStock);
                    return;
                }
            }

            // ensure at least one item transfers
            if (itemsToTransfer < 1)
            {
                NavigateTo(isBuyMode
                    ? MenuNames.InsufficientTech
                    : MenuNames.InsufficientStock);
                return;
            }
        }

        // everything checks out → Confirmation
        pendingTechAmount = techAmount;
        if (isBuyMode)
            confirmationMessage = String.Format("{0} {1} for {2} {3}?", FormatNumber(itemsToTransfer), selectedItem, techAmount, DEFAULT_CURRENCY_DISPLAY);
        else
            confirmationMessage = String.Format("Sell {0} {1} for {2} {3}?", FormatNumber(itemsToTransfer), selectedItem, techAmount, DEFAULT_CURRENCY_DISPLAY);

        transactionPending   = true;
        savedExchangeIndex   = selectedIndex;
        savedExchangeScroll  = firstVisibleIndex;
        NavigateTo(MenuNames.Confirmation);
        return;
    }

    // Confirmation → execute / cancel
    if (currentMenu == MenuNames.Confirmation)
    {
        string selOpt = menus[currentMenu][selectedIndex];
        if (selOpt == "Yes" && transactionPending)
        {
            if (isBuyMode)
                ProcessPurchase(selectedItem, pendingTechAmount);
            else
                ProcessSellTransaction(selectedItem, pendingTechAmount);

            transactionPending = false;
            GoBack();
            if (currentMenu == MenuNames.Exchange)
            {
                selectedIndex     = savedExchangeIndex;
                firstVisibleIndex = savedExchangeScroll;
            }
            UpdateLCD();
        }
        else if (selOpt == "No")
        {
            transactionPending = false;
            GoBack();
            if (currentMenu == MenuNames.Exchange)
            {
                selectedIndex     = savedExchangeIndex;
                firstVisibleIndex = savedExchangeScroll;
            }
            UpdateLCD();
        }
        return;
    }

    // Insufficient prompts → Back
    if (currentMenu == MenuNames.InsufficientTech || currentMenu == MenuNames.InsufficientStock)
    {
        if (menus[currentMenu][selectedIndex] == "Back")
            GoBack();
    }
}

/////////////////////////////////////////
// 11) STATE MACHINE LOGIC CALLS IN MAIN 
/////////////////////////////////////////

public void Main(string argument, UpdateType updateSource)
{
    // Early exit if we're running low on instruction count
    if(Runtime.CurrentInstructionCount > Runtime.MaxInstructionCount * 0.8)
        return;
    
    if(PBLimiter(argument))
        return;
    
    if(!AcquireLock())
        return;
    
    bool userInteracted = false;
    string argLower = argument.ToLower();
    
    // Handle user input
    switch(argLower)
    {
        case "1":
        case "green":
        case "select":
            userInteracted = true;
            HandleSelect();
            break;
        case "2":
        case "up":
            userInteracted = true;
            Navigate(-1);
            break;
        case "3":
        case "down":
            userInteracted = true;
            Navigate(1);
            break;
        case "4":
        case "red":
        case "back":
            userInteracted = true;
            GoBack();
            break;
        case "clear":
            if(debugLCD != null)
                debugLCD.WriteText("", false);
            ReleaseLock();
            return;
    }
    
    // Handle user interaction - cache inventory and update immediately
    if(userInteracted)
    {
        CacheInventoryData(); // Only cache when user interacts
        idleCounter = 0;
        UpdateLCD();
        ReleaseLock();
        return;
    }
    
    // Increment idle counter for automatic updates
    if((updateSource & (UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100)) != 0)
        idleCounter++;
    
    // Handle idle timeout
    if(idleCounter >= IDLE_THRESHOLD)
    {
        currentMenu = MenuNames.Welcome;
        menuStack.Clear();
        selectedIndex = 0;
        firstVisibleIndex = 0;
        UpdateLCD();
        idleCounter = 0;
    }
    
    // Always update trade preview (lightweight operation)
    UpdateTradePreview(lcdTradePreview, TradeContainer);
    
    // Cache inventory data periodically when needed
    bool shouldCacheInventory = (tickCounter % 10 == 0) || 
                               (_purchaseState != PurchaseState.Idle) || 
                               (_saleState != SaleState.Idle);
    
    if(shouldCacheInventory)
        CacheInventoryData();
    
    // Always run state machines for responsive trading
    RunPurchaseStateMachine();
    RunSaleStateMachine();
    
    // Update price displays less frequently (lightweight background task)
    if(tickCounter % 30 == 0)
        UpdatePriceDisplays();
    
    tickCounter++;
    
    // Update displays based on threshold
    if(tickCounter >= TICK_THRESHOLD)
    {
        tickCounter = 0;
        UpdateLCD();
        UpdateAdminLCD();
    }
    
    // Release lock when both state machines are idle
    if(_purchaseState == PurchaseState.Idle && _saleState == SaleState.Idle)
        ReleaseLock();
}


////////////////////////
// 12) PROCESS PURCHASE 
////////////////////////

void ProcessPurchase(string itemName, int techDeposit)
{
    string rawKey = GetRawKey(itemName);
    if (!buyPrices.ContainsKey(rawKey))
    {
        // No price defined → abort
        Log($"Error: No buy price for {itemName}");
        LogTransaction($"FAILED: No buy price set for {itemName}.");
        return;
    }

    // 2a. Check station stock
    long storeHas = GetAvailableInStore(itemName, true);
    bool isIngot = menus[MenuNames.BuyIngots].Contains(itemName);
    long itemsToMove;
    int techToSpend;
    MyItemType itemTypeObj;

    if (isIngot)
    {
        // integer‐rate ingot purchase: e.g. 1Tech = 1000000kg Iron
        long rateInt        = (long)buyPrices[rawKey];
        long reserved       = rateInt * 1;                           // always keep at least 1Tech’s worth in stock
        long availableSale  = Math.Max(0, storeHas - reserved);
        int  maxUnits       = (int)(availableSale / rateInt);

        techToSpend  = Math.Min(techDeposit, maxUnits);
        if (techToSpend < 1)
        {
            // either not enough stock or not enough deposited Tech
            NavigateTo(storeHas - reserved < rateInt
                ? MenuNames.InsufficientStock
                : MenuNames.InsufficientTech);
            return;
        }
        itemsToMove  = rateInt * techToSpend;
        itemTypeObj  = MyItemType.MakeIngot(rawKey);
    }
    else
    {
        // fractional/component purchase
        long wanted   = (long)Math.Floor(buyPrices[rawKey] * techDeposit);
        long available= Math.Min(wanted, storeHas);
        if (available < 1)
        {
            NavigateTo(storeHas < 1
                ? MenuNames.InsufficientStock
                : MenuNames.InsufficientTech);
            return;
        }
        itemsToMove   = available;
        techToSpend   = (int)Math.Ceiling(itemsToMove / buyPrices[rawKey]);
        if (menus[MenuNames.BuyAmmunition].Contains(itemName))
            itemTypeObj = MyItemType.Parse("MyObjectBuilder_AmmoMagazine/" + rawKey);
        else
            itemTypeObj = MyItemType.MakeComponent(rawKey);
    }

    // 2b. Check TradeContainer actually holds the deposited Tech
    int depositedTech = GetTech8xInTradeContainer(false);
    if (depositedTech < techToSpend)
    {
        NavigateTo(MenuNames.InsufficientTech);
        return;
    }

    // 2c. All checks passed → OPEN SORTERS A & C, and go into Phase0_Staging
    OpenGate(_sorterA);  // “STOREINVENTORY → HoldingA” valve
    OpenGate(_sorterC);  // “TradeContainer → HoldingB” valve

    _pm_ItemName   = itemName;    // e.g. “Iron”
    _pm_ItemQty    = itemsToMove; // e.g. 1000000
    _pm_TechAmount = techToSpend; // e.g.1

    _purchaseState = PurchaseState.Phase0_Staging;
}


//////////////////////////////////
// 13) PROCESS SELL TRANSACTION 
//////////////////////////////////

void ProcessSellTransaction(string itemName, int techAmount)
{
    // 1) Lookup price & compute required items in TradeContainer
    string rawKey = GetRawKey(itemName);
    if (!sellPrices.ContainsKey(rawKey))
    {
        Log($"Error: No sell price set for {itemName}");
        LogTransaction($"FAILED: No sell price set for {itemName}. Transaction aborted.");
        return;
    }
    double rate        = sellPrices[rawKey];
    long   requiredQty = (long)Math.Round(rate * techAmount);

    // 2a) Check TradeContainer for enough of that item
    var tradeInv      = TradeContainer.GetInventory();
    bool isIngot      = menus[MenuNames.SellIngots].Contains(itemName);
    bool isAmmo       = menus[MenuNames.SellAmmunition].Contains(itemName);
    MyItemType itemType;
    if (isAmmo)
        itemType = MyItemType.Parse("MyObjectBuilder_AmmoMagazine/" + rawKey);
    else if (isIngot)
        itemType = MyItemType.MakeIngot(rawKey);
    else
        itemType = MyItemType.MakeComponent(rawKey);

    long haveInTrade = (long)tradeInv.GetItemAmount(itemType);
    if (haveInTrade < requiredQty)
    {
        NavigateTo(MenuNames.InsufficientStock);
        return;
    }

    // 2b) Check store’s available room: storeHas + requiredQty <= limit
    long storeHas = GetAvailableInStore(itemName, true);
    long limit    = 0;
    if (menus[MenuNames.SellIngots].Contains(itemName))
        limit = maxIngotLimits[itemName];
    else if (menus[MenuNames.SellComponents].Contains(itemName))
        limit = maxComponentLimits[itemName];
    else if (menus[MenuNames.SellModdedComponents].Contains(itemName))
        limit = maxModdedComponentLimits[itemName];
    else if (menus[MenuNames.SellAmmunition].Contains(itemName))
        limit = maxAmmoLimits[itemName];

    if (storeHas + requiredQty > limit)
    {
        NavigateTo(MenuNames.InsufficientStock);
        return;
    }

    // 2c) Check vault has enough Tech to pay
    int vaultTech = (int)techVault.GetInventory().GetItemAmount(
        MyItemType.MakeComponent(DEFAULT_CURRENCY_TYPE));
    if (vaultTech < techAmount)
    {
        NavigateTo(MenuNames.InsufficientTech);
        return;
    }

    // 3) All checks passed → OPEN Sorter A (TechVault → HoldingA) & Sorter C (TradeContainer → HoldingB)
    OpenGate(_sorterA);
    OpenGate(_sorterC);

    _sm_ItemName   = itemName;      // e.g. "Iron"
    _sm_ItemQty    = requiredQty;   // e.g. 1 200 000
    _sm_TechAmount = techAmount;    // e.g. 1

    _saleState = SaleState.Phase0_Staging;
}
/////////////////////////////////
// 15) RUNPURCHASESTATEMACHINE
////////////////////////////////

void RunPurchaseStateMachine()
{
    if (_purchaseState == PurchaseState.Idle) return;

    // — Resolve types —
    MyItemType itemType = ResolveItemType(_pm_ItemName);
    MyItemType techType = MyItemType.MakeComponent(DEFAULT_CURRENCY_TYPE);

    // — Read current counts in holdings —
    long holdingA_Count = (long)_holdingA.GetInventory().GetItemAmount(itemType);
    long holdingB_Count = (long)_holdingB.GetInventory().GetItemAmount(techType);

    Log($"[PM] State={_purchaseState}, A={holdingA_Count}/{_pm_ItemQty}, " +
        $"B={holdingB_Count}/{_pm_TechAmount}, " +
        $"AOpen={_sorterA.Enabled}, COpen={_sorterC.Enabled}, " +
        $"BOpen={_sorterB.Enabled}, DOpen={_sorterD.Enabled}", true);

    switch (_purchaseState)
    {
        // --------------------------------------------
        // Phase0_Staging: open A/C, TransferAny into holdings
        // --------------------------------------------
        case PurchaseState.Phase0_Staging:
            // (1) Ensure SortersA &C are open
            if (!_sorterA.Enabled) OpenGate(_sorterA);
            if (!_sorterC.Enabled) OpenGate(_sorterC);

            // (2a) Script‐driven move: STORE → HoldingA
            if (holdingA_Count < _pm_ItemQty)
            {
                long needed  = _pm_ItemQty - holdingA_Count;
                long movedSoFar = holdingA_Count;

                foreach (var store in storeContainers)
                {
                    if (movedSoFar >= _pm_ItemQty) break;
                    long movedNow = TransferAny(
                        store.GetInventory(),
                        _holdingA.GetInventory(),
                        itemType,
                        _pm_ItemQty - movedSoFar
                    );
                    movedSoFar += movedNow;
                }
                holdingA_Count = (long)_holdingA.GetInventory().GetItemAmount(itemType);
                Log($"[PM] Staging: HoldingA now {holdingA_Count}/{_pm_ItemQty}", true);
            }

            // (2b) Script‐driven move: TradeContainer → HoldingB
            if (holdingB_Count < _pm_TechAmount)
            {
                long neededTech = _pm_TechAmount - holdingB_Count;
                long movedTech = TransferAny(
                    TradeContainer.GetInventory(),
                    _holdingB.GetInventory(),
                    techType,
                    neededTech
                );
                holdingB_Count += movedTech;
                Log($"[PM] Staging: HoldingB now {holdingB_Count}/{_pm_TechAmount} Tech", true);
            }

            // (3) If both holdings are ≥ target, close A/C and go Phase1
            if (holdingA_Count >= _pm_ItemQty && holdingB_Count >= _pm_TechAmount)
            {
                CloseGate(_sorterA);
                CloseGate(_sorterC);

                // Now “unlock” final flow: open B &D (valves)
                OpenGate(_sorterB); // HoldingA → TradeContainer
                OpenGate(_sorterD); // HoldingB → TechVault

                _purchaseState = PurchaseState.Phase1_Final;
                Log("[PM] Phase0 complete → closed A/C, opened B/D for final‐flow", true);
            }
            break;

        // --------------------------------------------------------
        // Phase1_Final: script calls TransferAny while B/D are open
        // --------------------------------------------------------
        case PurchaseState.Phase1_Final:
            // (1) If SorterB is open, move from HoldingA → TradeContainer
            if (_sorterB.Enabled)
            {
                var holdingAInv = _holdingA.GetInventory();
                var tradeInv    = TradeContainer.GetInventory();
                long movedItems = TransferAny(
                    holdingAInv,
                    tradeInv,
                    itemType,
                    _pm_ItemQty
                );
                Log($"[PM] Phase1: Moved {movedItems}/{_pm_ItemQty} {itemType.SubtypeId} to customer via SorterB", true);
            }

            // (2) If SorterD is open, move from HoldingB → TechVault
            if (_sorterD.Enabled)
            {
                var holdingBInv = _holdingB.GetInventory();
                var vaultInv    = techVault.GetInventory();
                long movedTech = TransferAny(
                    holdingBInv,
                    vaultInv,
                    techType,
                    _pm_TechAmount
                );
                Log($"[PM] Phase1: Moved {movedTech}/{_pm_TechAmount} Tech to vault via SorterD", true);
            }

            // (3) Re‐read holding counts
            long aLeft = (long)_holdingA.GetInventory().GetItemAmount(itemType);
            long bLeft = (long)_holdingB.GetInventory().GetItemAmount(techType);

            Log($"[PM] Phase1: ALeft={aLeft}, BLeft={bLeft}, " +
                $"BOpen={_sorterB.Enabled}, DOpen={_sorterD.Enabled}", true);

            // (4) Once both holdings read zero, we know the final transfer is done
            if (aLeft == 0 && bLeft == 0)
            {
                // Close final‐flow valves
                CloseGate(_sorterB);
                CloseGate(_sorterD);

                _purchaseState = PurchaseState.Idle;
                Log("[PM] Phase1 complete → all holdings drained, sorters B/D closed", true);

                LogTransaction($"SUCCESS: purchased {_pm_ItemQty} {_pm_ItemName} for {_pm_TechAmount} {DEFAULT_CURRENCY_DISPLAY}");
                UpdateLCD();
            }
            break;
    }
}
////////////////////////////////////////////////////////////////
// 16) RUNSALESTATEMACHINE: Drives the “sell” through HoldingA/HoldingB → TechVault/StoreContainers
////////////////////////////////////////////////////////////////
void RunSaleStateMachine()
{
    if (_saleState == SaleState.Idle) return;

    // Resolve types
    MyItemType itemType = ResolveItemType(_sm_ItemName);
    MyItemType techType = MyItemType.MakeComponent(DEFAULT_CURRENCY_TYPE);

    // (1) Read how much is in each Holding
    long holdingA_Count = (long)_holdingA.GetInventory().GetItemAmount(techType);   // Tech in HoldingA
    long holdingB_Count = (long)_holdingB.GetInventory().GetItemAmount(itemType);  // Item in HoldingB

    Log($"[SM] State={_saleState}, A={holdingA_Count}/{_sm_TechAmount}, " +
        $"B={holdingB_Count}/{_sm_ItemQty}, " +
        $"AOpen={_sorterA.Enabled}, COpen={_sorterC.Enabled}, " +
        $"BOpen={_sorterB.Enabled}, DOpen={_sorterD.Enabled}", 
        true);

    switch (_saleState)
    {
        // ------------------------------------------------------------
        // Phase0_Staging: open A/C, TransferAny into holdings
        // ------------------------------------------------------------
        case SaleState.Phase0_Staging:
            // (a) Ensure SortersA &C are open
            if (!_sorterA.Enabled) OpenGate(_sorterA);
            if (!_sorterC.Enabled) OpenGate(_sorterC);

            // (b) Scripted staging: TechVault → HoldingA
            if (holdingA_Count < _sm_TechAmount)
            {
                long neededTech = _sm_TechAmount - holdingA_Count;
                long movedTech = TransferAny(
                    techVault.GetInventory(),
                    _holdingA.GetInventory(),
                    techType,
                    neededTech
                );
                holdingA_Count += movedTech;
                Log($"[SM] Staging: HoldingA now {holdingA_Count}/{_sm_TechAmount} Tech", true);
            }

            // (c) Scripted staging: TradeContainer → HoldingB
            if (holdingB_Count < _sm_ItemQty)
            {
                long neededItem = _sm_ItemQty - holdingB_Count;
                long movedItem = TransferAny(
                    TradeContainer.GetInventory(),
                    _holdingB.GetInventory(),
                    itemType,
                    neededItem
                );
                holdingB_Count += movedItem;
                Log($"[SM] Staging: HoldingB now {holdingB_Count}/{_sm_ItemQty} {itemType.SubtypeId}", true);
            }

            // (d) Once both holdings are full, close A/C and open B/D
            if (holdingA_Count >= _sm_TechAmount && holdingB_Count >= _sm_ItemQty)
            {
                CloseGate(_sorterA);
                CloseGate(_sorterC);

                // Open final valves: SorterB (HoldingA → TradeContainer) and SorterD (HoldingB → StoreInventory)
                OpenGate(_sorterB);
                OpenGate(_sorterD);

                _saleState = SaleState.Phase1_Final;
                Log("[SM] Phase0 complete → closed A/C, opened B/D for final‐flow", true);
            }
            break;

        // ---------------------------------------------------------------
        // Phase1_Final: script‐driven transfers while B/D are open
        // ---------------------------------------------------------------
        case SaleState.Phase1_Final:
            // (1) If SorterB is open, move Tech from HoldingA → TradeContainer
            if (_sorterB.Enabled)
            {
                var holdAInv = _holdingA.GetInventory();
                var tradeInv = TradeContainer.GetInventory();
                long movedTech = TransferAny(
                    holdAInv,
                    tradeInv,
                    techType,
                    _sm_TechAmount
                );
                Log($"[SM] Phase1: Moved {movedTech}/{_sm_TechAmount} Tech to customer via SorterB", true);
            }

            // (2) If SorterD is open, move item from HoldingB → storeContainers
            if (_sorterD.Enabled)
            {
                long movedTotal = 0;
                var holdBInv    = _holdingB.GetInventory();
                foreach (var store in storeContainers)
                {
                    if (movedTotal >= _sm_ItemQty) break;
                    movedTotal += TransferAny(
                        holdBInv,
                        store.GetInventory(),
                        itemType,
                        _sm_ItemQty - movedTotal
                    );
                }
                Log($"[SM] Phase1: Moved {movedTotal}/{_sm_ItemQty} {itemType.SubtypeId} to store via SorterD", true);
            }

            // (3) Re‐read each Holding
            long aLeft = (long)_holdingA.GetInventory().GetItemAmount(techType);
            long bLeft = (long)_holdingB.GetInventory().GetItemAmount(itemType);

            Log($"[SM] Phase1: ALeft={aLeft}, BLeft={bLeft}, " +
                $"BOpen={_sorterB.Enabled}, DOpen={_sorterD.Enabled}", 
                true);

            // (4) Once both are empty, close B/D and finish
            if (aLeft == 0 && bLeft == 0)
            {
                CloseGate(_sorterB);
                CloseGate(_sorterD);

                _saleState = SaleState.Idle;
                Log("[SM] Phase1 complete → all sorters closed", true);

                LogTransaction($"SUCCESS: sold {_sm_ItemQty} {_sm_ItemName} for {_sm_TechAmount} {DEFAULT_CURRENCY_DISPLAY}");
                UpdateLCD();
            }
            break;
    }
}

///////////////////
// 17) GATE HELPERS 
///////////////////

void OpenGate(IMyConveyorSorter sorter)
{
    if (sorter != null) sorter.Enabled = true;
}

void CloseGate(IMyConveyorSorter sorter)
{
    if (sorter != null) sorter.Enabled = false;
}

///////////////////////////////////
// 18) DEDUCT & ROLLBACK CURRENCY 
///////////////////////////////////

bool DeductCurrency(int techAmount)
{
    var tradeInv = TradeContainer.GetInventory();
    var vaultInv = techVault.GetInventory();
    var currency = MyItemType.MakeComponent(DEFAULT_CURRENCY_TYPE);

    long deposited = (long)tradeInv.GetItemAmount(currency);
    if (deposited < techAmount)
    {
        Log($"Error: only {deposited} Tech deposited, cannot deduct {techAmount}.");
        LogTransaction($"FAILED: Insufficient {DEFAULT_CURRENCY_DISPLAY} deposited ({deposited}/{techAmount}).");
        return false;
    }

    long moved = TransferAny(tradeInv, vaultInv, currency, techAmount);
    if (moved != techAmount)
    {
        if (moved > 0)
            TransferAny(vaultInv, tradeInv, currency, moved);

        Log($"Error: moved only {moved}/{techAmount} Tech to vault, rolling back.");
        LogTransaction($"FAILED: could only move {moved}/{techAmount} {DEFAULT_CURRENCY_DISPLAY} to vault.");
        return false;
    }
    return true;
}

/////////////////////
// 19) TRANSFERHELPER 
/////////////////////

long TransferAny(IMyInventory source, IMyInventory dest, MyItemType type, long maxAmount)
{
    long movedTotal = 0;
    var items = new List<MyInventoryItem>();
    source.GetItems(items);

    foreach (var invItem in items)
    {
        if (invItem.Type != type) 
            continue;

        long available = (long)invItem.Amount;
        long want      = System.Math.Min(maxAmount - movedTotal, available);
        if (want <= 0) break;

        long before = (long)source.GetItemAmount(type);
        source.TransferItemTo(dest, invItem, (MyFixedPoint)(double)want);
        long after = (long)source.GetItemAmount(type);
        long actualMoved = before - after;

        if (actualMoved > 0)
        {
            movedTotal += actualMoved;
            if (movedTotal >= maxAmount) break;
        }
    }
    return movedTotal;
}
