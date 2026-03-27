using Npgsql;

namespace TraceabilityDriver.Tests.TestDatabase
{
    public class TestPostgreSQLDatabase : ITestDatabase
    {
        private readonly TestDatabaseConfig _config;

        public TestPostgreSQLDatabase(TestDatabaseConfig config)
        {
            _config = config;
        }

        public void SetupDatabase()
        {
            // Clear all pooled Npgsql connections before touching the database to
            // prevent 57P01 "terminating connection" error caused by attempting to use
            // a pooled connection that has been killed on the server.
            NpgsqlConnection.ClearAllPools();

            string serverConnectionString = _config.ConnectionString
                .Replace($"Database={_config.DatabaseName}", "Database=postgres");

            const int maxAttempts = 3;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using (var connection = new NpgsqlConnection(serverConnectionString))
                    {
                        connection.Open();

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = $@"
                                SELECT pg_terminate_backend(pg_stat_activity.pid)
                                FROM pg_stat_activity
                                WHERE pg_stat_activity.datname = '{_config.DatabaseName}'
                                AND pid <> pg_backend_pid();";
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{_config.DatabaseName}\";";
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = $"CREATE DATABASE \"{_config.DatabaseName}\";";
                            cmd.ExecuteNonQuery();
                        }
                    }

                    using (var connection = new NpgsqlConnection(_config.ConnectionString))
                    {
                        connection.Open();

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = GetBuildSql();
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = connection.CreateCommand())
                        {
                            cmd.CommandText = GetSeedSql();
                            cmd.ExecuteNonQuery();
                        }
                    }

                    return;
                }
                catch (NpgsqlException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(500 * attempt);
                    NpgsqlConnection.ClearAllPools();
                }
            }
        }

        private static string GetBuildSql()
        {
            return @"
CREATE SCHEMA IF NOT EXISTS src;

CREATE TABLE src.party (
    partyid BIGSERIAL PRIMARY KEY,
    partycode VARCHAR(50) NOT NULL UNIQUE,
    partyname VARCHAR(200) NOT NULL,
    gln VARCHAR(50) NULL,
    pgln VARCHAR(50) NULL,
    country CHAR(2) NULL,
    createdutc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

CREATE TABLE src.location (
    locationid BIGSERIAL PRIMARY KEY,
    locationcode VARCHAR(50) NOT NULL UNIQUE,
    locationname VARCHAR(200) NOT NULL,
    locationtype VARCHAR(50) NOT NULL,
    ownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    country CHAR(2) NULL,
    gln VARCHAR(50) NULL,
    registrationnumber VARCHAR(100) NULL,
    createdutc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

CREATE TABLE src.vessel (
    vesselid BIGSERIAL PRIMARY KEY,
    vesselcode VARCHAR(50) NOT NULL UNIQUE,
    vesselname VARCHAR(200) NOT NULL,
    flagcountry CHAR(2) NULL,
    registrationnumber VARCHAR(100) NULL,
    vessellocationid BIGINT NULL REFERENCES src.location(locationid),
    ownerpartyid BIGINT NULL REFERENCES src.party(partyid)
);

CREATE TABLE src.species (
    speciesid BIGSERIAL PRIMARY KEY,
    scientificname VARCHAR(200) NOT NULL,
    commonname VARCHAR(200) NULL,
    faocode VARCHAR(20) NULL
);

CREATE TABLE src.productdefinition (
    productdefinitionid BIGSERIAL PRIMARY KEY,
    ownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    gtin VARCHAR(50) NULL,
    shortdescription VARCHAR(200) NOT NULL,
    productformcode VARCHAR(30) NOT NULL,
    speciesid BIGINT NULL REFERENCES src.species(speciesid)
);

CREATE TABLE src.certificate (
    certificateid BIGSERIAL PRIMARY KEY,
    certificatetype VARCHAR(80) NOT NULL,
    certificatenumber VARCHAR(120) NOT NULL,
    issuerpartyid BIGINT NULL REFERENCES src.party(partyid),
    validfrom DATE NULL,
    validto DATE NULL
);

CREATE TABLE src.lot (
    lotid BIGSERIAL PRIMARY KEY,
    lotcode VARCHAR(80) NOT NULL UNIQUE,
    productdefinitionid BIGINT NOT NULL REFERENCES src.productdefinition(productdefinitionid),
    ownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    productionmethod VARCHAR(50) NULL,
    createdutc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

CREATE TABLE src.logisticunit (
    logisticunitid BIGSERIAL PRIMARY KEY,
    sscc VARCHAR(50) NOT NULL UNIQUE,
    ownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    createdutc TIMESTAMP NOT NULL DEFAULT (NOW() AT TIME ZONE 'UTC')
);

CREATE TABLE src.logisticunitlot (
    logisticunitid BIGINT NOT NULL REFERENCES src.logisticunit(logisticunitid),
    lotid BIGINT NOT NULL REFERENCES src.lot(lotid),
    quantity DECIMAL(18,3) NULL,
    uom VARCHAR(10) NULL,
    PRIMARY KEY (logisticunitid, lotid)
);

CREATE TABLE src.fishingtrip (
    fishingtripid BIGSERIAL PRIMARY KEY,
    tripnumber VARCHAR(50) NOT NULL UNIQUE,
    vesselid BIGINT NOT NULL REFERENCES src.vessel(vesselid),
    operatorpartyid BIGINT NOT NULL REFERENCES src.party(partyid),
    startutc TIMESTAMP NOT NULL,
    endutc TIMESTAMP NULL
);

CREATE TABLE src.fishingactivity (
    fishingactivityid BIGSERIAL PRIMARY KEY,
    fishingtripid BIGINT NOT NULL REFERENCES src.fishingtrip(fishingtripid),
    activitynumber VARCHAR(50) NOT NULL,
    eventtimeutc TIMESTAMP NOT NULL,
    catcharea VARCHAR(120) NULL,
    geartypecode VARCHAR(50) NULL,
    gpsavailable BOOLEAN NOT NULL DEFAULT FALSE,
    fishingauthcertid BIGINT NULL REFERENCES src.certificate(certificateid),
    humanpolicycertid BIGINT NULL REFERENCES src.certificate(certificateid),
    UNIQUE (fishingtripid, activitynumber)
);

CREATE TABLE src.fishingcatchline (
    fishingcatchlineid BIGSERIAL PRIMARY KEY,
    fishingactivityid BIGINT NOT NULL REFERENCES src.fishingactivity(fishingactivityid),
    lotid BIGINT NOT NULL REFERENCES src.lot(lotid),
    quantity DECIMAL(18,3) NOT NULL,
    uom VARCHAR(10) NOT NULL DEFAULT 'KGM'
);

CREATE TABLE src.landing (
    landingid BIGSERIAL PRIMARY KEY,
    landingnumber VARCHAR(50) NOT NULL UNIQUE,
    vesselid BIGINT NOT NULL REFERENCES src.vessel(vesselid),
    portlocationid BIGINT NOT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    harvestcertid BIGINT NULL REFERENCES src.certificate(certificateid),
    humanpolicycertid BIGINT NULL REFERENCES src.certificate(certificateid)
);

CREATE TABLE src.landingline (
    landinglineid BIGSERIAL PRIMARY KEY,
    landingid BIGINT NOT NULL REFERENCES src.landing(landingid),
    lotid BIGINT NOT NULL REFERENCES src.lot(lotid),
    quantity DECIMAL(18,3) NOT NULL,
    uom VARCHAR(10) NOT NULL DEFAULT 'KGM'
);

CREATE TABLE src.transshipment (
    transshipmentid BIGSERIAL PRIMARY KEY,
    transshipmentnumber VARCHAR(50) NOT NULL UNIQUE,
    fromvesselid BIGINT NULL REFERENCES src.vessel(vesselid),
    tovesselid BIGINT NULL REFERENCES src.vessel(vesselid),
    atlocationid BIGINT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    transportnumber VARCHAR(60) NULL,
    transporttype VARCHAR(30) NULL
);

CREATE TABLE src.transshipmentline (
    transshipmentlineid BIGSERIAL PRIMARY KEY,
    transshipmentid BIGINT NOT NULL REFERENCES src.transshipment(transshipmentid),
    lotid BIGINT NULL REFERENCES src.lot(lotid),
    logisticunitid BIGINT NULL REFERENCES src.logisticunit(logisticunitid),
    quantity DECIMAL(18,3) NULL,
    uom VARCHAR(10) NULL
);

CREATE TABLE src.processingbatch (
    processingbatchid BIGSERIAL PRIMARY KEY,
    batchnumber VARCHAR(60) NOT NULL UNIQUE,
    facilitylocationid BIGINT NOT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    processingtypecode VARCHAR(60) NULL,
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    coccertid BIGINT NULL REFERENCES src.certificate(certificateid),
    humanpolicycertid BIGINT NULL REFERENCES src.certificate(certificateid)
);

CREATE TABLE src.processinginput (
    processinginputid BIGSERIAL PRIMARY KEY,
    processingbatchid BIGINT NOT NULL REFERENCES src.processingbatch(processingbatchid),
    lotid BIGINT NOT NULL REFERENCES src.lot(lotid),
    quantity DECIMAL(18,3) NOT NULL,
    uom VARCHAR(10) NOT NULL DEFAULT 'KGM'
);

CREATE TABLE src.processingoutput (
    processingoutputid BIGSERIAL PRIMARY KEY,
    processingbatchid BIGINT NOT NULL REFERENCES src.processingbatch(processingbatchid),
    lotid BIGINT NOT NULL REFERENCES src.lot(lotid),
    quantity DECIMAL(18,3) NOT NULL,
    uom VARCHAR(10) NOT NULL DEFAULT 'KGM'
);

CREATE TABLE src.aggregationevent (
    aggregationeventid BIGSERIAL PRIMARY KEY,
    eventnumber VARCHAR(60) NOT NULL UNIQUE,
    eventtype VARCHAR(30) NOT NULL,
    locationid BIGINT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid)
);

CREATE TABLE src.aggregationline (
    aggregationlineid BIGSERIAL PRIMARY KEY,
    aggregationeventid BIGINT NOT NULL REFERENCES src.aggregationevent(aggregationeventid),
    parentlogisticunitid BIGINT NOT NULL REFERENCES src.logisticunit(logisticunitid),
    childlogisticunitid BIGINT NULL REFERENCES src.logisticunit(logisticunitid),
    childlotid BIGINT NULL REFERENCES src.lot(lotid),
    quantity DECIMAL(18,3) NULL,
    uom VARCHAR(10) NULL
);

CREATE TABLE src.shipment (
    shipmentid BIGSERIAL PRIMARY KEY,
    shipmentnumber VARCHAR(60) NOT NULL UNIQUE,
    shipfromlocationid BIGINT NOT NULL REFERENCES src.location(locationid),
    shiptolocationid BIGINT NOT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    carrierpartyid BIGINT NULL REFERENCES src.party(partyid),
    transporttype VARCHAR(30) NULL,
    transportvehicleid VARCHAR(80) NULL,
    transportnumber VARCHAR(80) NULL,
    transportproviderid VARCHAR(80) NULL,
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    coccertid BIGINT NULL REFERENCES src.certificate(certificateid),
    humanpolicycertid BIGINT NULL REFERENCES src.certificate(certificateid)
);

CREATE TABLE src.shipmentline (
    shipmentlineid BIGSERIAL PRIMARY KEY,
    shipmentid BIGINT NOT NULL REFERENCES src.shipment(shipmentid),
    lotid BIGINT NULL REFERENCES src.lot(lotid),
    logisticunitid BIGINT NULL REFERENCES src.logisticunit(logisticunitid),
    quantity DECIMAL(18,3) NULL,
    uom VARCHAR(10) NULL
);

CREATE TABLE src.receipt (
    receiptid BIGSERIAL PRIMARY KEY,
    receiptnumber VARCHAR(60) NOT NULL UNIQUE,
    receiveatlocationid BIGINT NOT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    supplierpartyid BIGINT NULL REFERENCES src.party(partyid),
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    coccertid BIGINT NULL REFERENCES src.certificate(certificateid),
    humanpolicycertid BIGINT NULL REFERENCES src.certificate(certificateid)
);

CREATE TABLE src.receiptline (
    receiptlineid BIGSERIAL PRIMARY KEY,
    receiptid BIGINT NOT NULL REFERENCES src.receipt(receiptid),
    lotid BIGINT NULL REFERENCES src.lot(lotid),
    logisticunitid BIGINT NULL REFERENCES src.logisticunit(logisticunitid),
    quantity DECIMAL(18,3) NULL,
    uom VARCHAR(10) NULL
);

CREATE TABLE src.storageevent (
    storageeventid BIGSERIAL PRIMARY KEY,
    storageeventnumber VARCHAR(60) NOT NULL UNIQUE,
    locationid BIGINT NOT NULL REFERENCES src.location(locationid),
    eventtimeutc TIMESTAMP NOT NULL,
    informationproviderpartyid BIGINT NULL REFERENCES src.party(partyid),
    productownerpartyid BIGINT NULL REFERENCES src.party(partyid),
    coccertid BIGINT NULL REFERENCES src.certificate(certificateid),
    humanpolicycertid BIGINT NULL REFERENCES src.certificate(certificateid)
);

CREATE TABLE src.storageline (
    storagelineid BIGSERIAL PRIMARY KEY,
    storageeventid BIGINT NOT NULL REFERENCES src.storageevent(storageeventid),
    lotid BIGINT NULL REFERENCES src.lot(lotid),
    logisticunitid BIGINT NULL REFERENCES src.logisticunit(logisticunitid),
    quantity DECIMAL(18,3) NULL,
    uom VARCHAR(10) NULL
);

CREATE INDEX ix_fishingactivity_sync ON src.fishingactivity(fishingactivityid);
CREATE INDEX ix_landing_sync ON src.landing(landingid);
CREATE INDEX ix_transshipment_sync ON src.transshipment(transshipmentid);
CREATE INDEX ix_processingbatch_sync ON src.processingbatch(processingbatchid);
CREATE INDEX ix_shipment_sync ON src.shipment(shipmentid);
CREATE INDEX ix_receipt_sync ON src.receipt(receiptid);
CREATE INDEX ix_aggregationevent_sync ON src.aggregationevent(aggregationeventid);
";
        }

        private static string GetSeedSql()
        {
            return @"
DO $$
DECLARE
    v_party_op001 BIGINT;
    v_party_carr01 BIGINT;
    v_party_plant01 BIGINT;
    v_party_ware01 BIGINT;
    v_party_buy001 BIGINT;
    v_loc_vessel BIGINT;
    v_loc_port BIGINT;
    v_loc_plant BIGINT;
    v_loc_ware BIGINT;
    v_loc_buydc BIGINT;
    v_vessel_vsl001 BIGINT;
    v_species_cod BIGINT;
    v_species_had BIGINT;
    v_pd_codraw BIGINT;
    v_pd_codfillet BIGINT;
    v_pd_hadraw BIGINT;
    v_cert_fishingauth BIGINT;
    v_cert_coc BIGINT;
    v_cert_human BIGINT;
    v_cert_harvest BIGINT;
    v_lot_codraw BIGINT;
    v_lot_hadraw BIGINT;
    v_lot_codflt BIGINT;
    v_sscc_1 BIGINT;
    v_sscc_2 BIGINT;
    v_tripid BIGINT;
    v_act1 BIGINT;
    v_act2 BIGINT;
    v_landingid BIGINT;
    v_tsid BIGINT;
    v_batchid BIGINT;
    v_fhbatchid BIGINT;
    v_fmtbatchid BIGINT;
    v_aggaddid BIGINT;
    v_aggcomid BIGINT;
    v_aggdelid BIGINT;
    v_shipid BIGINT;
    v_rcvid BIGINT;
    v_stoid BIGINT;
BEGIN
    -- Clear tables
    DELETE FROM src.storageline;
    DELETE FROM src.storageevent;
    DELETE FROM src.receiptline;
    DELETE FROM src.receipt;
    DELETE FROM src.shipmentline;
    DELETE FROM src.shipment;
    DELETE FROM src.aggregationline;
    DELETE FROM src.aggregationevent;
    DELETE FROM src.processingoutput;
    DELETE FROM src.processinginput;
    DELETE FROM src.processingbatch;
    DELETE FROM src.transshipmentline;
    DELETE FROM src.transshipment;
    DELETE FROM src.landingline;
    DELETE FROM src.landing;
    DELETE FROM src.fishingcatchline;
    DELETE FROM src.fishingactivity;
    DELETE FROM src.fishingtrip;
    DELETE FROM src.logisticunitlot;
    DELETE FROM src.logisticunit;
    DELETE FROM src.lot;
    DELETE FROM src.certificate;
    DELETE FROM src.productdefinition;
    DELETE FROM src.species;
    DELETE FROM src.vessel;
    DELETE FROM src.location;
    DELETE FROM src.party;

    -- Parties
    INSERT INTO src.party (partycode, partyname, country, gln, pgln) VALUES ('OP001', 'North Sea Fisheries Ltd', 'GB', NULL, NULL);
    INSERT INTO src.party (partycode, partyname, country, gln, pgln) VALUES ('CARR01', 'BlueWave Logistics', 'GB', NULL, NULL);
    INSERT INTO src.party (partycode, partyname, country, gln, pgln) VALUES ('PLANT01', 'Harbor Processing Plant', 'GB', NULL, NULL);
    INSERT INTO src.party (partycode, partyname, country, gln, pgln) VALUES ('WARE01', 'ColdStore Warehouse', 'GB', NULL, NULL);
    INSERT INTO src.party (partycode, partyname, country, gln, pgln) VALUES ('BUY001', 'Retail Buyer Co', 'GB', NULL, NULL);

    SELECT partyid INTO v_party_op001 FROM src.party WHERE partycode='OP001';
    SELECT partyid INTO v_party_carr01 FROM src.party WHERE partycode='CARR01';
    SELECT partyid INTO v_party_plant01 FROM src.party WHERE partycode='PLANT01';
    SELECT partyid INTO v_party_ware01 FROM src.party WHERE partycode='WARE01';
    SELECT partyid INTO v_party_buy001 FROM src.party WHERE partycode='BUY001';

    -- Locations
    INSERT INTO src.location (locationcode, locationname, locationtype, ownerpartyid, country, gln, registrationnumber) VALUES ('VSL_LOC_01', 'FV Northern Star (as location)', 'Vessel', v_party_op001, 'GB', NULL, 'GB-FV-NS-001');
    INSERT INTO src.location (locationcode, locationname, locationtype, ownerpartyid, country, gln, registrationnumber) VALUES ('PORT_01', 'Port of Grimsby', 'Port', NULL, 'GB', NULL, NULL);
    INSERT INTO src.location (locationcode, locationname, locationtype, ownerpartyid, country, gln, registrationnumber) VALUES ('PLANT_01', 'Harbor Processing Plant', 'Plant', v_party_plant01, 'GB', NULL, 'PLANT-GB-01');
    INSERT INTO src.location (locationcode, locationname, locationtype, ownerpartyid, country, gln, registrationnumber) VALUES ('WARE_01', 'ColdStore Warehouse', 'Warehouse', v_party_ware01, 'GB', NULL, 'WARE-GB-01');
    INSERT INTO src.location (locationcode, locationname, locationtype, ownerpartyid, country, gln, registrationnumber) VALUES ('BUY_DC_01', 'Retail Buyer DC', 'Warehouse', v_party_buy001, 'GB', NULL, 'DC-GB-01');

    SELECT locationid INTO v_loc_vessel FROM src.location WHERE locationcode='VSL_LOC_01';
    SELECT locationid INTO v_loc_port FROM src.location WHERE locationcode='PORT_01';
    SELECT locationid INTO v_loc_plant FROM src.location WHERE locationcode='PLANT_01';
    SELECT locationid INTO v_loc_ware FROM src.location WHERE locationcode='WARE_01';
    SELECT locationid INTO v_loc_buydc FROM src.location WHERE locationcode='BUY_DC_01';

    -- Vessel
    INSERT INTO src.vessel (vesselcode, vesselname, flagcountry, registrationnumber, vessellocationid, ownerpartyid) VALUES ('VSL001', 'FV Northern Star', 'GB', 'GB-FV-NS-001', v_loc_vessel, v_party_op001);
    SELECT vesselid INTO v_vessel_vsl001 FROM src.vessel WHERE vesselcode='VSL001';

    -- Species
    INSERT INTO src.species (scientificname, commonname, faocode) VALUES ('Gadus morhua', 'Atlantic cod', 'COD');
    INSERT INTO src.species (scientificname, commonname, faocode) VALUES ('Melanogrammus aeglefinus', 'Haddock', 'HAD');
    SELECT speciesid INTO v_species_cod FROM src.species WHERE scientificname='Gadus morhua';
    SELECT speciesid INTO v_species_had FROM src.species WHERE scientificname='Melanogrammus aeglefinus';

    -- Product definitions
    INSERT INTO src.productdefinition (ownerpartyid, gtin, shortdescription, productformcode, speciesid) VALUES (v_party_op001, '00012345600012', 'Atlantic Cod - Whole (Raw)', 'RAW', v_species_cod);
    INSERT INTO src.productdefinition (ownerpartyid, gtin, shortdescription, productformcode, speciesid) VALUES (v_party_plant01, '00012345600029', 'Atlantic Cod - Fillet (Chilled)', 'FILLET', v_species_cod);
    INSERT INTO src.productdefinition (ownerpartyid, gtin, shortdescription, productformcode, speciesid) VALUES (v_party_op001, '00012345600036', 'Haddock - Whole (Raw)', 'RAW', v_species_had);
    SELECT productdefinitionid INTO v_pd_codraw FROM src.productdefinition WHERE shortdescription LIKE 'Atlantic Cod - Whole%';
    SELECT productdefinitionid INTO v_pd_codfillet FROM src.productdefinition WHERE shortdescription LIKE 'Atlantic Cod - Fillet%';
    SELECT productdefinitionid INTO v_pd_hadraw FROM src.productdefinition WHERE shortdescription LIKE 'Haddock - Whole%';

    -- Certificates
    INSERT INTO src.certificate (certificatetype, certificatenumber, issuerpartyid, validfrom, validto) VALUES ('fishingAuth', 'FA-GB-2026-0001', v_party_op001, '2026-01-01', '2026-12-31');
    INSERT INTO src.certificate (certificatetype, certificatenumber, issuerpartyid, validfrom, validto) VALUES ('harvestCoC', 'COC-GB-PLANT-01', v_party_plant01, '2025-01-01', '2027-12-31');
    INSERT INTO src.certificate (certificatetype, certificatenumber, issuerpartyid, validfrom, validto) VALUES ('humanPolicy', 'HP-GB-0009', NULL, '2025-01-01', '2027-12-31');
    INSERT INTO src.certificate (certificatetype, certificatenumber, issuerpartyid, validfrom, validto) VALUES ('harvestCert', 'HC-GB-7777', NULL, '2025-01-01', '2027-12-31');
    SELECT certificateid INTO v_cert_fishingauth FROM src.certificate WHERE certificatetype='fishingAuth';
    SELECT certificateid INTO v_cert_coc FROM src.certificate WHERE certificatetype='harvestCoC';
    SELECT certificateid INTO v_cert_human FROM src.certificate WHERE certificatetype='humanPolicy';
    SELECT certificateid INTO v_cert_harvest FROM src.certificate WHERE certificatetype='harvestCert';

    -- Lots
    INSERT INTO src.lot (lotcode, productdefinitionid, ownerpartyid, productionmethod) VALUES ('LOT-COD-RAW-0001', v_pd_codraw, v_party_op001, 'wild');
    INSERT INTO src.lot (lotcode, productdefinitionid, ownerpartyid, productionmethod) VALUES ('LOT-HAD-RAW-0001', v_pd_hadraw, v_party_op001, 'wild');
    INSERT INTO src.lot (lotcode, productdefinitionid, ownerpartyid, productionmethod) VALUES ('LOT-COD-FLT-0001', v_pd_codfillet, v_party_plant01, 'wild');
    SELECT lotid INTO v_lot_codraw FROM src.lot WHERE lotcode='LOT-COD-RAW-0001';
    SELECT lotid INTO v_lot_hadraw FROM src.lot WHERE lotcode='LOT-HAD-RAW-0001';
    SELECT lotid INTO v_lot_codflt FROM src.lot WHERE lotcode='LOT-COD-FLT-0001';

    -- Logistic Units
    INSERT INTO src.logisticunit (sscc, ownerpartyid) VALUES ('000000000000000001', v_party_plant01);
    INSERT INTO src.logisticunit (sscc, ownerpartyid) VALUES ('000000000000000002', v_party_plant01);
    SELECT logisticunitid INTO v_sscc_1 FROM src.logisticunit WHERE sscc='000000000000000001';
    SELECT logisticunitid INTO v_sscc_2 FROM src.logisticunit WHERE sscc='000000000000000002';
    INSERT INTO src.logisticunitlot (logisticunitid, lotid, quantity, uom) VALUES (v_sscc_1, v_lot_codflt, 500.000, 'KGM');
    INSERT INTO src.logisticunitlot (logisticunitid, lotid, quantity, uom) VALUES (v_sscc_2, v_lot_codraw, 200.000, 'KGM');
    INSERT INTO src.logisticunitlot (logisticunitid, lotid, quantity, uom) VALUES (v_sscc_2, v_lot_hadraw, 150.000, 'KGM');

    -- Fishing Trip + Activities
    INSERT INTO src.fishingtrip (tripnumber, vesselid, operatorpartyid, startutc, endutc) VALUES ('TRIP-2026-0001', v_vessel_vsl001, v_party_op001, '2026-01-02 06:00:00', '2026-01-03 18:00:00');
    SELECT fishingtripid INTO v_tripid FROM src.fishingtrip WHERE tripnumber='TRIP-2026-0001';

    INSERT INTO src.fishingactivity (fishingtripid, activitynumber, eventtimeutc, catcharea, geartypecode, gpsavailable, fishingauthcertid, humanpolicycertid)
    VALUES (v_tripid, 'SET-001', '2026-01-02 10:30:00', 'urn:example:area:01', 'GEAR1', TRUE, v_cert_fishingauth, v_cert_human);
    SELECT fishingactivityid INTO v_act1 FROM src.fishingactivity WHERE fishingtripid=v_tripid AND activitynumber='SET-001';
    INSERT INTO src.fishingcatchline (fishingactivityid, lotid, quantity, uom) VALUES (v_act1, v_lot_codraw, 1000.000, 'KGM');

    INSERT INTO src.fishingactivity (fishingtripid, activitynumber, eventtimeutc, catcharea, geartypecode, gpsavailable, fishingauthcertid, humanpolicycertid)
    VALUES (v_tripid, 'SET-002', '2026-01-03 09:15:00', 'urn:example:area:02', 'GEAR9_9', TRUE, v_cert_fishingauth, v_cert_human);
    SELECT fishingactivityid INTO v_act2 FROM src.fishingactivity WHERE fishingtripid=v_tripid AND activitynumber='SET-002';
    INSERT INTO src.fishingcatchline (fishingactivityid, lotid, quantity, uom) VALUES (v_act2, v_lot_hadraw, 800.000, 'KGM');

    -- Landing
    INSERT INTO src.landing (landingnumber, vesselid, portlocationid, eventtimeutc, informationproviderpartyid, productownerpartyid, harvestcertid, humanpolicycertid)
    VALUES ('LAND-2026-0001', v_vessel_vsl001, v_loc_port, '2026-01-03 19:00:00', v_party_op001, v_party_op001, v_cert_harvest, v_cert_human);
    SELECT landingid INTO v_landingid FROM src.landing WHERE landingnumber='LAND-2026-0001';
    INSERT INTO src.landingline (landingid, lotid, quantity, uom) VALUES (v_landingid, v_lot_codraw, 950.000, 'KGM');
    INSERT INTO src.landingline (landingid, lotid, quantity, uom) VALUES (v_landingid, v_lot_hadraw, 780.000, 'KGM');

    -- Transshipment
    INSERT INTO src.transshipment (transshipmentnumber, fromvesselid, tovesselid, atlocationid, eventtimeutc, informationproviderpartyid, productownerpartyid, transportnumber, transporttype)
    VALUES ('TS-2026-0001', v_vessel_vsl001, NULL, v_loc_port, '2026-01-03 20:30:00', v_party_op001, v_party_op001, 'VOY-TS-01', 'vessel');
    SELECT transshipmentid INTO v_tsid FROM src.transshipment WHERE transshipmentnumber='TS-2026-0001';
    INSERT INTO src.transshipmentline (transshipmentid, lotid, logisticunitid, quantity, uom) VALUES (v_tsid, v_lot_codraw, NULL, 500.000, 'KGM');

    -- Processing batch
    INSERT INTO src.processingbatch (batchnumber, facilitylocationid, eventtimeutc, processingtypecode, informationproviderpartyid, productownerpartyid, coccertid, humanpolicycertid)
    VALUES ('PB-2026-0001', v_loc_plant, '2026-01-04 08:00:00', 'FILLETING', v_party_plant01, v_party_plant01, v_cert_coc, v_cert_human);
    SELECT processingbatchid INTO v_batchid FROM src.processingbatch WHERE batchnumber='PB-2026-0001';
    INSERT INTO src.processinginput (processingbatchid, lotid, quantity, uom) VALUES (v_batchid, v_lot_codraw, 500.000, 'KGM');
    INSERT INTO src.processingoutput (processingbatchid, lotid, quantity, uom) VALUES (v_batchid, v_lot_codflt, 450.000, 'KGM');

    -- Farm Harvest batch
    INSERT INTO src.processingbatch (batchnumber, facilitylocationid, eventtimeutc, processingtypecode, informationproviderpartyid, productownerpartyid, coccertid, humanpolicycertid)
    VALUES ('PB-2026-FH-0001', v_loc_plant, '2026-01-04 08:30:00', 'FARM_HARVEST', v_party_plant01, v_party_plant01, v_cert_coc, v_cert_human);
    SELECT processingbatchid INTO v_fhbatchid FROM src.processingbatch WHERE batchnumber='PB-2026-FH-0001';
    INSERT INTO src.processinginput (processingbatchid, lotid, quantity, uom) VALUES (v_fhbatchid, v_lot_codraw, 120.000, 'KGM');
    INSERT INTO src.processingoutput (processingbatchid, lotid, quantity, uom) VALUES (v_fhbatchid, v_lot_codflt, 100.000, 'KGM');

    -- Feedmill Transformation batch
    INSERT INTO src.processingbatch (batchnumber, facilitylocationid, eventtimeutc, processingtypecode, informationproviderpartyid, productownerpartyid, coccertid, humanpolicycertid)
    VALUES ('PB-2026-FMT-0001', v_loc_plant, '2026-01-04 09:30:00', 'FEEDMILL_TRANSFORMATION', v_party_plant01, v_party_plant01, v_cert_coc, v_cert_human);
    SELECT processingbatchid INTO v_fmtbatchid FROM src.processingbatch WHERE batchnumber='PB-2026-FMT-0001';
    INSERT INTO src.processinginput (processingbatchid, lotid, quantity, uom) VALUES (v_fmtbatchid, v_lot_codraw, 120.000, 'KGM');
    INSERT INTO src.processingoutput (processingbatchid, lotid, quantity, uom) VALUES (v_fmtbatchid, v_lot_codflt, 100.000, 'KGM');

    -- Aggregation ADD
    INSERT INTO src.aggregationevent (eventnumber, eventtype, locationid, eventtimeutc, informationproviderpartyid, productownerpartyid)
    VALUES ('AGG-2026-ADD-0001', 'ADD', v_loc_plant, '2026-01-04 10:00:00', v_party_plant01, v_party_plant01);
    SELECT aggregationeventid INTO v_aggaddid FROM src.aggregationevent WHERE eventnumber='AGG-2026-ADD-0001';
    INSERT INTO src.aggregationline (aggregationeventid, parentlogisticunitid, childlogisticunitid, childlotid, quantity, uom)
    VALUES (v_aggaddid, v_sscc_1, NULL, v_lot_codflt, 450.000, 'KGM');

    -- Commingling
    INSERT INTO src.aggregationevent (eventnumber, eventtype, locationid, eventtimeutc, informationproviderpartyid, productownerpartyid)
    VALUES ('AGG-2026-COM-0001', 'COMMINGLE', v_loc_ware, '2026-01-04 14:00:00', v_party_ware01, v_party_ware01);
    SELECT aggregationeventid INTO v_aggcomid FROM src.aggregationevent WHERE eventnumber='AGG-2026-COM-0001';
    INSERT INTO src.aggregationline (aggregationeventid, parentlogisticunitid, childlogisticunitid, childlotid, quantity, uom)
    VALUES (v_aggcomid, v_sscc_2, NULL, v_lot_codraw, 200.000, 'KGM');
    INSERT INTO src.aggregationline (aggregationeventid, parentlogisticunitid, childlogisticunitid, childlotid, quantity, uom)
    VALUES (v_aggcomid, v_sscc_2, NULL, v_lot_hadraw, 150.000, 'KGM');

    -- Disaggregation
    INSERT INTO src.aggregationevent (eventnumber, eventtype, locationid, eventtimeutc, informationproviderpartyid, productownerpartyid)
    VALUES ('AGG-2026-DEL-0001', 'DELETE', v_loc_ware, '2026-01-04 15:00:00', v_party_ware01, v_party_ware01);
    SELECT aggregationeventid INTO v_aggdelid FROM src.aggregationevent WHERE eventnumber='AGG-2026-DEL-0001';
    INSERT INTO src.aggregationline (aggregationeventid, parentlogisticunitid, childlogisticunitid, childlotid, quantity, uom)
    VALUES (v_aggdelid, v_sscc_2, NULL, v_lot_hadraw, 150.000, 'KGM');

    -- Shipment
    INSERT INTO src.shipment (shipmentnumber, shipfromlocationid, shiptolocationid, eventtimeutc, carrierpartyid, transporttype, transportvehicleid, transportnumber, transportproviderid, informationproviderpartyid, productownerpartyid, coccertid, humanpolicycertid)
    VALUES ('SHIP-2026-0001', v_loc_plant, v_loc_buydc, '2026-01-05 07:00:00', v_party_carr01, 'truck', 'TRUCK-77', 'BOL-9001', 'CARR01', v_party_plant01, v_party_plant01, v_cert_coc, v_cert_human);
    SELECT shipmentid INTO v_shipid FROM src.shipment WHERE shipmentnumber='SHIP-2026-0001';
    INSERT INTO src.shipmentline (shipmentid, lotid, logisticunitid, quantity, uom) VALUES (v_shipid, NULL, v_sscc_1, NULL, NULL);
    INSERT INTO src.shipmentline (shipmentid, lotid, logisticunitid, quantity, uom) VALUES (v_shipid, v_lot_codflt, NULL, 450.000, 'KGM');

    -- Receipt
    INSERT INTO src.receipt (receiptnumber, receiveatlocationid, eventtimeutc, supplierpartyid, informationproviderpartyid, productownerpartyid, coccertid, humanpolicycertid)
    VALUES ('RCV-2026-0001', v_loc_buydc, '2026-01-05 13:30:00', v_party_plant01, v_party_buy001, v_party_buy001, v_cert_coc, v_cert_human);
    SELECT receiptid INTO v_rcvid FROM src.receipt WHERE receiptnumber='RCV-2026-0001';
    INSERT INTO src.receiptline (receiptid, lotid, logisticunitid, quantity, uom) VALUES (v_rcvid, NULL, v_sscc_1, NULL, NULL);
    INSERT INTO src.receiptline (receiptid, lotid, logisticunitid, quantity, uom) VALUES (v_rcvid, v_lot_codflt, NULL, 450.000, 'KGM');

    -- Storage
    INSERT INTO src.storageevent (storageeventnumber, locationid, eventtimeutc, informationproviderpartyid, productownerpartyid, coccertid, humanpolicycertid)
    VALUES ('STO-2026-0001', v_loc_buydc, '2026-01-05 14:30:00', v_party_buy001, v_party_buy001, v_cert_coc, v_cert_human);
    SELECT storageeventid INTO v_stoid FROM src.storageevent WHERE storageeventnumber='STO-2026-0001';
    INSERT INTO src.storageline (storageeventid, lotid, logisticunitid, quantity, uom) VALUES (v_stoid, v_lot_codflt, NULL, 450.000, 'KGM');

END $$;
";
        }
    }
}
