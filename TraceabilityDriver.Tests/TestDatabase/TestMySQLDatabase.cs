using MySql.Data.MySqlClient;

namespace TraceabilityDriver.Tests.TestDatabase
{
    public class TestMySQLDatabase : ITestDatabase
    {
        private readonly TestDatabaseConfig _config;

        public TestMySQLDatabase(TestDatabaseConfig config)
        {
            _config = config;
        }

        public void SetupDatabase()
        {
            string serverConnectionString = _config.ConnectionString
                .Replace($"database={_config.DatabaseName}", "database=mysql");

            using (var connection = new MySqlConnection(serverConnectionString))
            {
                connection.Open();

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"DROP DATABASE IF EXISTS `{_config.DatabaseName}`;";
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = $"CREATE DATABASE `{_config.DatabaseName}`;";
                    cmd.ExecuteNonQuery();
                }
            }

            using (var connection = new MySqlConnection(_config.ConnectionString))
            {
                connection.Open();

                foreach (string statement in SplitStatements(GetBuildSql()))
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = statement;
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = GetSeedSql();
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static IEnumerable<string> SplitStatements(string sql)
        {
            foreach (var stmt in sql.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = stmt.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    yield return trimmed;
            }
        }

        private static string GetBuildSql()
        {
            return @"
CREATE TABLE Party (
    PartyId BIGINT AUTO_INCREMENT PRIMARY KEY,
    PartyCode VARCHAR(50) NOT NULL UNIQUE,
    PartyName VARCHAR(200) NOT NULL,
    Gln VARCHAR(50) NULL,
    Pgln VARCHAR(50) NULL,
    Country CHAR(2) NULL,
    CreatedUtc DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP())
);

CREATE TABLE Location (
    LocationId BIGINT AUTO_INCREMENT PRIMARY KEY,
    LocationCode VARCHAR(50) NOT NULL UNIQUE,
    LocationName VARCHAR(200) NOT NULL,
    LocationType VARCHAR(50) NOT NULL,
    OwnerPartyId BIGINT NULL,
    Country CHAR(2) NULL,
    Gln VARCHAR(50) NULL,
    RegistrationNumber VARCHAR(100) NULL,
    CreatedUtc DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
    FOREIGN KEY (OwnerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE Vessel (
    VesselId BIGINT AUTO_INCREMENT PRIMARY KEY,
    VesselCode VARCHAR(50) NOT NULL UNIQUE,
    VesselName VARCHAR(200) NOT NULL,
    FlagCountry CHAR(2) NULL,
    RegistrationNumber VARCHAR(100) NULL,
    VesselLocationId BIGINT NULL,
    OwnerPartyId BIGINT NULL,
    FOREIGN KEY (VesselLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (OwnerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE Species (
    SpeciesId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ScientificName VARCHAR(200) NOT NULL,
    CommonName VARCHAR(200) NULL,
    FaoCode VARCHAR(20) NULL
);

CREATE TABLE ProductDefinition (
    ProductDefinitionId BIGINT AUTO_INCREMENT PRIMARY KEY,
    OwnerPartyId BIGINT NULL,
    Gtin VARCHAR(50) NULL,
    ShortDescription VARCHAR(200) NOT NULL,
    ProductFormCode VARCHAR(30) NOT NULL,
    SpeciesId BIGINT NULL,
    FOREIGN KEY (OwnerPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (SpeciesId) REFERENCES Species(SpeciesId)
);

CREATE TABLE Certificate (
    CertificateId BIGINT AUTO_INCREMENT PRIMARY KEY,
    CertificateType VARCHAR(80) NOT NULL,
    CertificateNumber VARCHAR(120) NOT NULL,
    IssuerPartyId BIGINT NULL,
    ValidFrom DATE NULL,
    ValidTo DATE NULL,
    FOREIGN KEY (IssuerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE Lot (
    LotId BIGINT AUTO_INCREMENT PRIMARY KEY,
    LotCode VARCHAR(80) NOT NULL UNIQUE,
    ProductDefinitionId BIGINT NOT NULL,
    OwnerPartyId BIGINT NULL,
    ProductionMethod VARCHAR(50) NULL,
    CreatedUtc DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
    FOREIGN KEY (ProductDefinitionId) REFERENCES ProductDefinition(ProductDefinitionId),
    FOREIGN KEY (OwnerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE LogisticUnit (
    LogisticUnitId BIGINT AUTO_INCREMENT PRIMARY KEY,
    Sscc VARCHAR(50) NOT NULL UNIQUE,
    OwnerPartyId BIGINT NULL,
    CreatedUtc DATETIME NOT NULL DEFAULT (UTC_TIMESTAMP()),
    FOREIGN KEY (OwnerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE LogisticUnitLot (
    LogisticUnitId BIGINT NOT NULL,
    LotId BIGINT NOT NULL,
    Quantity DECIMAL(18,3) NULL,
    Uom VARCHAR(10) NULL,
    PRIMARY KEY (LogisticUnitId, LotId),
    FOREIGN KEY (LogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId)
);

CREATE TABLE FishingTrip (
    FishingTripId BIGINT AUTO_INCREMENT PRIMARY KEY,
    TripNumber VARCHAR(50) NOT NULL UNIQUE,
    VesselId BIGINT NOT NULL,
    OperatorPartyId BIGINT NOT NULL,
    StartUtc DATETIME NOT NULL,
    EndUtc DATETIME NULL,
    FOREIGN KEY (VesselId) REFERENCES Vessel(VesselId),
    FOREIGN KEY (OperatorPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE FishingActivity (
    FishingActivityId BIGINT AUTO_INCREMENT PRIMARY KEY,
    FishingTripId BIGINT NOT NULL,
    ActivityNumber VARCHAR(50) NOT NULL,
    EventTimeUtc DATETIME NOT NULL,
    CatchArea VARCHAR(120) NULL,
    GearTypeCode VARCHAR(50) NULL,
    GpsAvailable TINYINT(1) NOT NULL DEFAULT 0,
    FishingAuthCertId BIGINT NULL,
    HumanPolicyCertId BIGINT NULL,
    UNIQUE (FishingTripId, ActivityNumber),
    FOREIGN KEY (FishingTripId) REFERENCES FishingTrip(FishingTripId),
    FOREIGN KEY (FishingAuthCertId) REFERENCES Certificate(CertificateId),
    FOREIGN KEY (HumanPolicyCertId) REFERENCES Certificate(CertificateId)
);

CREATE TABLE FishingCatchLine (
    FishingCatchLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    FishingActivityId BIGINT NOT NULL,
    LotId BIGINT NOT NULL,
    Quantity DECIMAL(18,3) NOT NULL,
    Uom VARCHAR(10) NOT NULL DEFAULT 'KGM',
    FOREIGN KEY (FishingActivityId) REFERENCES FishingActivity(FishingActivityId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId)
);

CREATE TABLE Landing (
    LandingId BIGINT AUTO_INCREMENT PRIMARY KEY,
    LandingNumber VARCHAR(50) NOT NULL UNIQUE,
    VesselId BIGINT NOT NULL,
    PortLocationId BIGINT NOT NULL,
    EventTimeUtc DATETIME NOT NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    HarvestCertId BIGINT NULL,
    HumanPolicyCertId BIGINT NULL,
    FOREIGN KEY (VesselId) REFERENCES Vessel(VesselId),
    FOREIGN KEY (PortLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (HarvestCertId) REFERENCES Certificate(CertificateId),
    FOREIGN KEY (HumanPolicyCertId) REFERENCES Certificate(CertificateId)
);

CREATE TABLE LandingLine (
    LandingLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    LandingId BIGINT NOT NULL,
    LotId BIGINT NOT NULL,
    Quantity DECIMAL(18,3) NOT NULL,
    Uom VARCHAR(10) NOT NULL DEFAULT 'KGM',
    FOREIGN KEY (LandingId) REFERENCES Landing(LandingId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId)
);

CREATE TABLE Transshipment (
    TransshipmentId BIGINT AUTO_INCREMENT PRIMARY KEY,
    TransshipmentNumber VARCHAR(50) NOT NULL UNIQUE,
    FromVesselId BIGINT NULL,
    ToVesselId BIGINT NULL,
    AtLocationId BIGINT NULL,
    EventTimeUtc DATETIME NOT NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    TransportNumber VARCHAR(60) NULL,
    TransportType VARCHAR(30) NULL,
    FOREIGN KEY (FromVesselId) REFERENCES Vessel(VesselId),
    FOREIGN KEY (ToVesselId) REFERENCES Vessel(VesselId),
    FOREIGN KEY (AtLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE TransshipmentLine (
    TransshipmentLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    TransshipmentId BIGINT NOT NULL,
    LotId BIGINT NULL,
    LogisticUnitId BIGINT NULL,
    Quantity DECIMAL(18,3) NULL,
    Uom VARCHAR(10) NULL,
    FOREIGN KEY (TransshipmentId) REFERENCES Transshipment(TransshipmentId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId),
    FOREIGN KEY (LogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId)
);

CREATE TABLE ProcessingBatch (
    ProcessingBatchId BIGINT AUTO_INCREMENT PRIMARY KEY,
    BatchNumber VARCHAR(60) NOT NULL UNIQUE,
    FacilityLocationId BIGINT NOT NULL,
    EventTimeUtc DATETIME NOT NULL,
    ProcessingTypeCode VARCHAR(60) NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    CocCertId BIGINT NULL,
    HumanPolicyCertId BIGINT NULL,
    FOREIGN KEY (FacilityLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (CocCertId) REFERENCES Certificate(CertificateId),
    FOREIGN KEY (HumanPolicyCertId) REFERENCES Certificate(CertificateId)
);

CREATE TABLE ProcessingInput (
    ProcessingInputId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ProcessingBatchId BIGINT NOT NULL,
    LotId BIGINT NOT NULL,
    Quantity DECIMAL(18,3) NOT NULL,
    Uom VARCHAR(10) NOT NULL DEFAULT 'KGM',
    FOREIGN KEY (ProcessingBatchId) REFERENCES ProcessingBatch(ProcessingBatchId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId)
);

CREATE TABLE ProcessingOutput (
    ProcessingOutputId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ProcessingBatchId BIGINT NOT NULL,
    LotId BIGINT NOT NULL,
    Quantity DECIMAL(18,3) NOT NULL,
    Uom VARCHAR(10) NOT NULL DEFAULT 'KGM',
    FOREIGN KEY (ProcessingBatchId) REFERENCES ProcessingBatch(ProcessingBatchId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId)
);

CREATE TABLE AggregationEvent (
    AggregationEventId BIGINT AUTO_INCREMENT PRIMARY KEY,
    EventNumber VARCHAR(60) NOT NULL UNIQUE,
    EventType VARCHAR(30) NOT NULL,
    LocationId BIGINT NULL,
    EventTimeUtc DATETIME NOT NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    FOREIGN KEY (LocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId)
);

CREATE TABLE AggregationLine (
    AggregationLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    AggregationEventId BIGINT NOT NULL,
    ParentLogisticUnitId BIGINT NOT NULL,
    ChildLogisticUnitId BIGINT NULL,
    ChildLotId BIGINT NULL,
    Quantity DECIMAL(18,3) NULL,
    Uom VARCHAR(10) NULL,
    FOREIGN KEY (AggregationEventId) REFERENCES AggregationEvent(AggregationEventId),
    FOREIGN KEY (ParentLogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId),
    FOREIGN KEY (ChildLogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId),
    FOREIGN KEY (ChildLotId) REFERENCES Lot(LotId)
);

CREATE TABLE Shipment (
    ShipmentId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ShipmentNumber VARCHAR(60) NOT NULL UNIQUE,
    ShipFromLocationId BIGINT NOT NULL,
    ShipToLocationId BIGINT NOT NULL,
    EventTimeUtc DATETIME NOT NULL,
    CarrierPartyId BIGINT NULL,
    TransportType VARCHAR(30) NULL,
    TransportVehicleId VARCHAR(80) NULL,
    TransportNumber VARCHAR(80) NULL,
    TransportProviderId VARCHAR(80) NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    CocCertId BIGINT NULL,
    HumanPolicyCertId BIGINT NULL,
    FOREIGN KEY (ShipFromLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (ShipToLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (CarrierPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (CocCertId) REFERENCES Certificate(CertificateId),
    FOREIGN KEY (HumanPolicyCertId) REFERENCES Certificate(CertificateId)
);

CREATE TABLE ShipmentLine (
    ShipmentLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ShipmentId BIGINT NOT NULL,
    LotId BIGINT NULL,
    LogisticUnitId BIGINT NULL,
    Quantity DECIMAL(18,3) NULL,
    Uom VARCHAR(10) NULL,
    FOREIGN KEY (ShipmentId) REFERENCES Shipment(ShipmentId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId),
    FOREIGN KEY (LogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId)
);

CREATE TABLE Receipt (
    ReceiptId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ReceiptNumber VARCHAR(60) NOT NULL UNIQUE,
    ReceiveAtLocationId BIGINT NOT NULL,
    EventTimeUtc DATETIME NOT NULL,
    SupplierPartyId BIGINT NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    CocCertId BIGINT NULL,
    HumanPolicyCertId BIGINT NULL,
    FOREIGN KEY (ReceiveAtLocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (SupplierPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (CocCertId) REFERENCES Certificate(CertificateId),
    FOREIGN KEY (HumanPolicyCertId) REFERENCES Certificate(CertificateId)
);

CREATE TABLE ReceiptLine (
    ReceiptLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    ReceiptId BIGINT NOT NULL,
    LotId BIGINT NULL,
    LogisticUnitId BIGINT NULL,
    Quantity DECIMAL(18,3) NULL,
    Uom VARCHAR(10) NULL,
    FOREIGN KEY (ReceiptId) REFERENCES Receipt(ReceiptId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId),
    FOREIGN KEY (LogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId)
);

CREATE TABLE StorageEvent (
    StorageEventId BIGINT AUTO_INCREMENT PRIMARY KEY,
    StorageEventNumber VARCHAR(60) NOT NULL UNIQUE,
    LocationId BIGINT NOT NULL,
    EventTimeUtc DATETIME NOT NULL,
    InformationProviderPartyId BIGINT NULL,
    ProductOwnerPartyId BIGINT NULL,
    CocCertId BIGINT NULL,
    HumanPolicyCertId BIGINT NULL,
    FOREIGN KEY (LocationId) REFERENCES Location(LocationId),
    FOREIGN KEY (InformationProviderPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (ProductOwnerPartyId) REFERENCES Party(PartyId),
    FOREIGN KEY (CocCertId) REFERENCES Certificate(CertificateId),
    FOREIGN KEY (HumanPolicyCertId) REFERENCES Certificate(CertificateId)
);

CREATE TABLE StorageLine (
    StorageLineId BIGINT AUTO_INCREMENT PRIMARY KEY,
    StorageEventId BIGINT NOT NULL,
    LotId BIGINT NULL,
    LogisticUnitId BIGINT NULL,
    Quantity DECIMAL(18,3) NULL,
    Uom VARCHAR(10) NULL,
    FOREIGN KEY (StorageEventId) REFERENCES StorageEvent(StorageEventId),
    FOREIGN KEY (LotId) REFERENCES Lot(LotId),
    FOREIGN KEY (LogisticUnitId) REFERENCES LogisticUnit(LogisticUnitId)
);

CREATE INDEX IX_FishingActivity_Sync ON FishingActivity(FishingActivityId);
CREATE INDEX IX_Landing_Sync ON Landing(LandingId);
CREATE INDEX IX_Transshipment_Sync ON Transshipment(TransshipmentId);
CREATE INDEX IX_ProcessingBatch_Sync ON ProcessingBatch(ProcessingBatchId);
CREATE INDEX IX_Shipment_Sync ON Shipment(ShipmentId);
CREATE INDEX IX_Receipt_Sync ON Receipt(ReceiptId);
CREATE INDEX IX_AggregationEvent_Sync ON AggregationEvent(AggregationEventId)
";
        }

        private static string GetSeedSql()
        {
            return @"
DELETE FROM StorageLine;
DELETE FROM StorageEvent;
DELETE FROM ReceiptLine;
DELETE FROM Receipt;
DELETE FROM ShipmentLine;
DELETE FROM Shipment;
DELETE FROM AggregationLine;
DELETE FROM AggregationEvent;
DELETE FROM ProcessingOutput;
DELETE FROM ProcessingInput;
DELETE FROM ProcessingBatch;
DELETE FROM TransshipmentLine;
DELETE FROM Transshipment;
DELETE FROM LandingLine;
DELETE FROM Landing;
DELETE FROM FishingCatchLine;
DELETE FROM FishingActivity;
DELETE FROM FishingTrip;
DELETE FROM LogisticUnitLot;
DELETE FROM LogisticUnit;
DELETE FROM Lot;
DELETE FROM Certificate;
DELETE FROM ProductDefinition;
DELETE FROM Species;
DELETE FROM Vessel;
DELETE FROM Location;
DELETE FROM Party;

INSERT INTO Party (PartyCode, PartyName, Country, Gln, Pgln) VALUES ('OP001', 'North Sea Fisheries Ltd', 'GB', NULL, NULL);
INSERT INTO Party (PartyCode, PartyName, Country, Gln, Pgln) VALUES ('CARR01', 'BlueWave Logistics', 'GB', NULL, NULL);
INSERT INTO Party (PartyCode, PartyName, Country, Gln, Pgln) VALUES ('PLANT01', 'Harbor Processing Plant', 'GB', NULL, NULL);
INSERT INTO Party (PartyCode, PartyName, Country, Gln, Pgln) VALUES ('WARE01', 'ColdStore Warehouse', 'GB', NULL, NULL);
INSERT INTO Party (PartyCode, PartyName, Country, Gln, Pgln) VALUES ('BUY001', 'Retail Buyer Co', 'GB', NULL, NULL);

SET @Party_OP001 = (SELECT PartyId FROM Party WHERE PartyCode='OP001');
SET @Party_CARR01 = (SELECT PartyId FROM Party WHERE PartyCode='CARR01');
SET @Party_PLANT01 = (SELECT PartyId FROM Party WHERE PartyCode='PLANT01');
SET @Party_WARE01 = (SELECT PartyId FROM Party WHERE PartyCode='WARE01');
SET @Party_BUY001 = (SELECT PartyId FROM Party WHERE PartyCode='BUY001');

INSERT INTO Location (LocationCode, LocationName, LocationType, OwnerPartyId, Country, Gln, RegistrationNumber) VALUES ('VSL_LOC_01', 'FV Northern Star (as location)', 'Vessel', @Party_OP001, 'GB', NULL, 'GB-FV-NS-001');
INSERT INTO Location (LocationCode, LocationName, LocationType, OwnerPartyId, Country, Gln, RegistrationNumber) VALUES ('PORT_01', 'Port of Grimsby', 'Port', NULL, 'GB', NULL, NULL);
INSERT INTO Location (LocationCode, LocationName, LocationType, OwnerPartyId, Country, Gln, RegistrationNumber) VALUES ('PLANT_01', 'Harbor Processing Plant', 'Plant', @Party_PLANT01, 'GB', NULL, 'PLANT-GB-01');
INSERT INTO Location (LocationCode, LocationName, LocationType, OwnerPartyId, Country, Gln, RegistrationNumber) VALUES ('WARE_01', 'ColdStore Warehouse', 'Warehouse', @Party_WARE01, 'GB', NULL, 'WARE-GB-01');
INSERT INTO Location (LocationCode, LocationName, LocationType, OwnerPartyId, Country, Gln, RegistrationNumber) VALUES ('BUY_DC_01', 'Retail Buyer DC', 'Warehouse', @Party_BUY001, 'GB', NULL, 'DC-GB-01');

SET @Loc_Vessel = (SELECT LocationId FROM Location WHERE LocationCode='VSL_LOC_01');
SET @Loc_Port = (SELECT LocationId FROM Location WHERE LocationCode='PORT_01');
SET @Loc_Plant = (SELECT LocationId FROM Location WHERE LocationCode='PLANT_01');
SET @Loc_Ware = (SELECT LocationId FROM Location WHERE LocationCode='WARE_01');
SET @Loc_BuyDC = (SELECT LocationId FROM Location WHERE LocationCode='BUY_DC_01');

INSERT INTO Vessel (VesselCode, VesselName, FlagCountry, RegistrationNumber, VesselLocationId, OwnerPartyId) VALUES ('VSL001', 'FV Northern Star', 'GB', 'GB-FV-NS-001', @Loc_Vessel, @Party_OP001);
SET @Vessel_VSL001 = (SELECT VesselId FROM Vessel WHERE VesselCode='VSL001');

INSERT INTO Species (ScientificName, CommonName, FaoCode) VALUES ('Gadus morhua', 'Atlantic cod', 'COD');
INSERT INTO Species (ScientificName, CommonName, FaoCode) VALUES ('Melanogrammus aeglefinus', 'Haddock', 'HAD');
SET @Species_Cod = (SELECT SpeciesId FROM Species WHERE ScientificName='Gadus morhua');
SET @Species_Had = (SELECT SpeciesId FROM Species WHERE ScientificName='Melanogrammus aeglefinus');

INSERT INTO ProductDefinition (OwnerPartyId, Gtin, ShortDescription, ProductFormCode, SpeciesId) VALUES (@Party_OP001, '00012345600012', 'Atlantic Cod - Whole (Raw)', 'RAW', @Species_Cod);
INSERT INTO ProductDefinition (OwnerPartyId, Gtin, ShortDescription, ProductFormCode, SpeciesId) VALUES (@Party_PLANT01, '00012345600029', 'Atlantic Cod - Fillet (Chilled)', 'FILLET', @Species_Cod);
INSERT INTO ProductDefinition (OwnerPartyId, Gtin, ShortDescription, ProductFormCode, SpeciesId) VALUES (@Party_OP001, '00012345600036', 'Haddock - Whole (Raw)', 'RAW', @Species_Had);
SET @PD_CodRaw = (SELECT ProductDefinitionId FROM ProductDefinition WHERE ShortDescription LIKE 'Atlantic Cod - Whole%');
SET @PD_CodFillet = (SELECT ProductDefinitionId FROM ProductDefinition WHERE ShortDescription LIKE 'Atlantic Cod - Fillet%');
SET @PD_HadRaw = (SELECT ProductDefinitionId FROM ProductDefinition WHERE ShortDescription LIKE 'Haddock - Whole%');

INSERT INTO Certificate (CertificateType, CertificateNumber, IssuerPartyId, ValidFrom, ValidTo) VALUES ('fishingAuth', 'FA-GB-2026-0001', @Party_OP001, '2026-01-01', '2026-12-31');
INSERT INTO Certificate (CertificateType, CertificateNumber, IssuerPartyId, ValidFrom, ValidTo) VALUES ('harvestCoC', 'COC-GB-PLANT-01', @Party_PLANT01, '2025-01-01', '2027-12-31');
INSERT INTO Certificate (CertificateType, CertificateNumber, IssuerPartyId, ValidFrom, ValidTo) VALUES ('humanPolicy', 'HP-GB-0009', NULL, '2025-01-01', '2027-12-31');
INSERT INTO Certificate (CertificateType, CertificateNumber, IssuerPartyId, ValidFrom, ValidTo) VALUES ('harvestCert', 'HC-GB-7777', NULL, '2025-01-01', '2027-12-31');
SET @Cert_FishingAuth = (SELECT CertificateId FROM Certificate WHERE CertificateType='fishingAuth');
SET @Cert_CoC = (SELECT CertificateId FROM Certificate WHERE CertificateType='harvestCoC');
SET @Cert_Human = (SELECT CertificateId FROM Certificate WHERE CertificateType='humanPolicy');
SET @Cert_Harvest = (SELECT CertificateId FROM Certificate WHERE CertificateType='harvestCert');

INSERT INTO Lot (LotCode, ProductDefinitionId, OwnerPartyId, ProductionMethod) VALUES ('LOT-COD-RAW-0001', @PD_CodRaw, @Party_OP001, 'wild');
INSERT INTO Lot (LotCode, ProductDefinitionId, OwnerPartyId, ProductionMethod) VALUES ('LOT-HAD-RAW-0001', @PD_HadRaw, @Party_OP001, 'wild');
INSERT INTO Lot (LotCode, ProductDefinitionId, OwnerPartyId, ProductionMethod) VALUES ('LOT-COD-FLT-0001', @PD_CodFillet, @Party_PLANT01, 'wild');
SET @Lot_CodRaw = (SELECT LotId FROM Lot WHERE LotCode='LOT-COD-RAW-0001');
SET @Lot_HadRaw = (SELECT LotId FROM Lot WHERE LotCode='LOT-HAD-RAW-0001');
SET @Lot_CodFlt = (SELECT LotId FROM Lot WHERE LotCode='LOT-COD-FLT-0001');

INSERT INTO LogisticUnit (Sscc, OwnerPartyId) VALUES ('000000000000000001', @Party_PLANT01);
INSERT INTO LogisticUnit (Sscc, OwnerPartyId) VALUES ('000000000000000002', @Party_PLANT01);
SET @SSCC_1 = (SELECT LogisticUnitId FROM LogisticUnit WHERE Sscc='000000000000000001');
SET @SSCC_2 = (SELECT LogisticUnitId FROM LogisticUnit WHERE Sscc='000000000000000002');
INSERT INTO LogisticUnitLot (LogisticUnitId, LotId, Quantity, Uom) VALUES (@SSCC_1, @Lot_CodFlt, 500.000, 'KGM');
INSERT INTO LogisticUnitLot (LogisticUnitId, LotId, Quantity, Uom) VALUES (@SSCC_2, @Lot_CodRaw, 200.000, 'KGM');
INSERT INTO LogisticUnitLot (LogisticUnitId, LotId, Quantity, Uom) VALUES (@SSCC_2, @Lot_HadRaw, 150.000, 'KGM');

INSERT INTO FishingTrip (TripNumber, VesselId, OperatorPartyId, StartUtc, EndUtc) VALUES ('TRIP-2026-0001', @Vessel_VSL001, @Party_OP001, '2026-01-02 06:00:00', '2026-01-03 18:00:00');
SET @TripId = (SELECT FishingTripId FROM FishingTrip WHERE TripNumber='TRIP-2026-0001');

INSERT INTO FishingActivity (FishingTripId, ActivityNumber, EventTimeUtc, CatchArea, GearTypeCode, GpsAvailable, FishingAuthCertId, HumanPolicyCertId)
VALUES (@TripId, 'SET-001', '2026-01-02 10:30:00', 'urn:example:area:01', 'GEAR1', 1, @Cert_FishingAuth, @Cert_Human);
SET @Act1 = (SELECT FishingActivityId FROM FishingActivity WHERE FishingTripId=@TripId AND ActivityNumber='SET-001');
INSERT INTO FishingCatchLine (FishingActivityId, LotId, Quantity, Uom) VALUES (@Act1, @Lot_CodRaw, 1000.000, 'KGM');

INSERT INTO FishingActivity (FishingTripId, ActivityNumber, EventTimeUtc, CatchArea, GearTypeCode, GpsAvailable, FishingAuthCertId, HumanPolicyCertId)
VALUES (@TripId, 'SET-002', '2026-01-03 09:15:00', 'urn:example:area:02', 'GEAR9_9', 1, @Cert_FishingAuth, @Cert_Human);
SET @Act2 = (SELECT FishingActivityId FROM FishingActivity WHERE FishingTripId=@TripId AND ActivityNumber='SET-002');
INSERT INTO FishingCatchLine (FishingActivityId, LotId, Quantity, Uom) VALUES (@Act2, @Lot_HadRaw, 800.000, 'KGM');

INSERT INTO Landing (LandingNumber, VesselId, PortLocationId, EventTimeUtc, InformationProviderPartyId, ProductOwnerPartyId, HarvestCertId, HumanPolicyCertId)
VALUES ('LAND-2026-0001', @Vessel_VSL001, @Loc_Port, '2026-01-03 19:00:00', @Party_OP001, @Party_OP001, @Cert_Harvest, @Cert_Human);
SET @LandingId = (SELECT LandingId FROM Landing WHERE LandingNumber='LAND-2026-0001');
INSERT INTO LandingLine (LandingId, LotId, Quantity, Uom) VALUES (@LandingId, @Lot_CodRaw, 950.000, 'KGM');
INSERT INTO LandingLine (LandingId, LotId, Quantity, Uom) VALUES (@LandingId, @Lot_HadRaw, 780.000, 'KGM');

INSERT INTO Transshipment (TransshipmentNumber, FromVesselId, ToVesselId, AtLocationId, EventTimeUtc, InformationProviderPartyId, ProductOwnerPartyId, TransportNumber, TransportType)
VALUES ('TS-2026-0001', @Vessel_VSL001, NULL, @Loc_Port, '2026-01-03 20:30:00', @Party_OP001, @Party_OP001, 'VOY-TS-01', 'vessel');
SET @TSId = (SELECT TransshipmentId FROM Transshipment WHERE TransshipmentNumber='TS-2026-0001');
INSERT INTO TransshipmentLine (TransshipmentId, LotId, LogisticUnitId, Quantity, Uom) VALUES (@TSId, @Lot_CodRaw, NULL, 500.000, 'KGM');

INSERT INTO ProcessingBatch (BatchNumber, FacilityLocationId, EventTimeUtc, ProcessingTypeCode, InformationProviderPartyId, ProductOwnerPartyId, CocCertId, HumanPolicyCertId)
VALUES ('PB-2026-0001', @Loc_Plant, '2026-01-04 08:00:00', 'FILLETING', @Party_PLANT01, @Party_PLANT01, @Cert_CoC, @Cert_Human);
SET @BatchId = (SELECT ProcessingBatchId FROM ProcessingBatch WHERE BatchNumber='PB-2026-0001');
INSERT INTO ProcessingInput (ProcessingBatchId, LotId, Quantity, Uom) VALUES (@BatchId, @Lot_CodRaw, 500.000, 'KGM');
INSERT INTO ProcessingOutput (ProcessingBatchId, LotId, Quantity, Uom) VALUES (@BatchId, @Lot_CodFlt, 450.000, 'KGM');

INSERT INTO AggregationEvent (EventNumber, EventType, LocationId, EventTimeUtc, InformationProviderPartyId, ProductOwnerPartyId)
VALUES ('AGG-2026-ADD-0001', 'ADD', @Loc_Plant, '2026-01-04 10:00:00', @Party_PLANT01, @Party_PLANT01);
SET @AggAddId = (SELECT AggregationEventId FROM AggregationEvent WHERE EventNumber='AGG-2026-ADD-0001');
INSERT INTO AggregationLine (AggregationEventId, ParentLogisticUnitId, ChildLogisticUnitId, ChildLotId, Quantity, Uom)
VALUES (@AggAddId, @SSCC_1, NULL, @Lot_CodFlt, 450.000, 'KGM');

INSERT INTO AggregationEvent (EventNumber, EventType, LocationId, EventTimeUtc, InformationProviderPartyId, ProductOwnerPartyId)
VALUES ('AGG-2026-COM-0001', 'COMMINGLE', @Loc_Ware, '2026-01-04 14:00:00', @Party_WARE01, @Party_WARE01);
SET @AggComId = (SELECT AggregationEventId FROM AggregationEvent WHERE EventNumber='AGG-2026-COM-0001');
INSERT INTO AggregationLine (AggregationEventId, ParentLogisticUnitId, ChildLogisticUnitId, ChildLotId, Quantity, Uom)
VALUES (@AggComId, @SSCC_2, NULL, @Lot_CodRaw, 200.000, 'KGM');
INSERT INTO AggregationLine (AggregationEventId, ParentLogisticUnitId, ChildLogisticUnitId, ChildLotId, Quantity, Uom)
VALUES (@AggComId, @SSCC_2, NULL, @Lot_HadRaw, 150.000, 'KGM');

INSERT INTO AggregationEvent (EventNumber, EventType, LocationId, EventTimeUtc, InformationProviderPartyId, ProductOwnerPartyId)
VALUES ('AGG-2026-DEL-0001', 'DELETE', @Loc_Ware, '2026-01-04 15:00:00', @Party_WARE01, @Party_WARE01);
SET @AggDelId = (SELECT AggregationEventId FROM AggregationEvent WHERE EventNumber='AGG-2026-DEL-0001');
INSERT INTO AggregationLine (AggregationEventId, ParentLogisticUnitId, ChildLogisticUnitId, ChildLotId, Quantity, Uom)
VALUES (@AggDelId, @SSCC_2, NULL, @Lot_HadRaw, 150.000, 'KGM');

INSERT INTO Shipment (ShipmentNumber, ShipFromLocationId, ShipToLocationId, EventTimeUtc, CarrierPartyId, TransportType, TransportVehicleId, TransportNumber, TransportProviderId, InformationProviderPartyId, ProductOwnerPartyId, CocCertId, HumanPolicyCertId)
VALUES ('SHIP-2026-0001', @Loc_Plant, @Loc_BuyDC, '2026-01-05 07:00:00', @Party_CARR01, 'truck', 'TRUCK-77', 'BOL-9001', 'CARR01', @Party_PLANT01, @Party_PLANT01, @Cert_CoC, @Cert_Human);
SET @ShipId = (SELECT ShipmentId FROM Shipment WHERE ShipmentNumber='SHIP-2026-0001');
INSERT INTO ShipmentLine (ShipmentId, LotId, LogisticUnitId, Quantity, Uom) VALUES (@ShipId, NULL, @SSCC_1, NULL, NULL);
INSERT INTO ShipmentLine (ShipmentId, LotId, LogisticUnitId, Quantity, Uom) VALUES (@ShipId, @Lot_CodFlt, NULL, 450.000, 'KGM');

INSERT INTO Receipt (ReceiptNumber, ReceiveAtLocationId, EventTimeUtc, SupplierPartyId, InformationProviderPartyId, ProductOwnerPartyId, CocCertId, HumanPolicyCertId)
VALUES ('RCV-2026-0001', @Loc_BuyDC, '2026-01-05 13:30:00', @Party_PLANT01, @Party_BUY001, @Party_BUY001, @Cert_CoC, @Cert_Human);
SET @RcvId = (SELECT ReceiptId FROM Receipt WHERE ReceiptNumber='RCV-2026-0001');
INSERT INTO ReceiptLine (ReceiptId, LotId, LogisticUnitId, Quantity, Uom) VALUES (@RcvId, NULL, @SSCC_1, NULL, NULL);
INSERT INTO ReceiptLine (ReceiptId, LotId, LogisticUnitId, Quantity, Uom) VALUES (@RcvId, @Lot_CodFlt, NULL, 450.000, 'KGM');

INSERT INTO StorageEvent (StorageEventNumber, LocationId, EventTimeUtc, InformationProviderPartyId, ProductOwnerPartyId, CocCertId, HumanPolicyCertId)
VALUES ('STO-2026-0001', @Loc_BuyDC, '2026-01-05 14:30:00', @Party_BUY001, @Party_BUY001, @Cert_CoC, @Cert_Human);
SET @StoId = (SELECT StorageEventId FROM StorageEvent WHERE StorageEventNumber='STO-2026-0001');
INSERT INTO StorageLine (StorageEventId, LotId, LogisticUnitId, Quantity, Uom) VALUES (@StoId, @Lot_CodFlt, NULL, 450.000, 'KGM');
";
        }
    }
}
