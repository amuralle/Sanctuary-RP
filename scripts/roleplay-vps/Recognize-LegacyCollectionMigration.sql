-- RP used an earlier ID for the same collection schema. Keep its history and
-- recognize the upstream ID only after verifying the existing table contract.
DROP PROCEDURE IF EXISTS RecognizeLegacyCollectionMigration;
DELIMITER //
CREATE PROCEDURE RecognizeLegacyCollectionMigration()
BEGIN
    IF EXISTS (SELECT 1 FROM __EFMigrationsHistory
               WHERE MigrationId = '20260924142002_AddCharacterCollectionEntries')
       AND NOT EXISTS (SELECT 1 FROM __EFMigrationsHistory
                       WHERE MigrationId = '20260928145039_AddCharacterCollectionEntries') THEN
        IF (SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CollectionEntries') <> 4
           OR (SELECT COUNT(*) FROM information_schema.COLUMNS
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CollectionEntries'
               AND IS_NULLABLE = 'NO' AND
                 ((COLUMN_NAME IN ('CollectionId', 'EntryId') AND DATA_TYPE = 'int')
                  OR (COLUMN_NAME = 'CharacterId' AND COLUMN_TYPE = 'bigint(20) unsigned')
                  OR (COLUMN_NAME = 'Collected' AND DATA_TYPE = 'datetime' AND DATETIME_PRECISION = 6
                      AND COLUMN_DEFAULT = 'current_timestamp(6)'))) <> 4
           OR COALESCE((SELECT GROUP_CONCAT(COLUMN_NAME ORDER BY ORDINAL_POSITION)
               FROM information_schema.KEY_COLUMN_USAGE
               WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CollectionEntries'
               AND CONSTRAINT_NAME = 'PRIMARY'), '') <> 'CharacterId,CollectionId,EntryId'
           OR NOT EXISTS (SELECT 1 FROM information_schema.KEY_COLUMN_USAGE
                          WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CollectionEntries'
                          AND COLUMN_NAME = 'CharacterId' AND REFERENCED_TABLE_NAME = 'Characters'
                          AND REFERENCED_COLUMN_NAME = 'Id')
           OR NOT EXISTS (SELECT 1 FROM information_schema.REFERENTIAL_CONSTRAINTS
                          WHERE CONSTRAINT_SCHEMA = DATABASE() AND TABLE_NAME = 'CollectionEntries'
                          AND REFERENCED_TABLE_NAME = 'Characters' AND DELETE_RULE = 'CASCADE') THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Legacy collection schema differs; refusing migration alias';
        END IF;
        INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion)
        VALUES ('20260928145039_AddCharacterCollectionEntries', '9.0.17');
    END IF;
END //
DELIMITER ;
CALL RecognizeLegacyCollectionMigration();
DROP PROCEDURE RecognizeLegacyCollectionMigration;
