DECLARE 
	@propertyTypeAlias AS VARCHAR(1000),
	@oldCompositionAlias AS VARCHAR(1000),
	@newCompositionAlias AS VARCHAR(1000),
	@oldCompositionId AS INT,
    @newCompositionId AS INT,
	@aliasesToPerformTheSwitchOn AS VARCHAR(1000),
	@database AS VARCHAR(1000)
--CONFIGURE - these are the only options that should be tampered with. This script is tested to work on Umbraco 8 - please backup or clone DB before running
--It will replace the old composition with the new composition on all pages defined here.
--It will copy the configuration of all properties on the old composition to the new properties on the new composition, you have to change them afterwards.
SET @propertyTypeAlias = 'gridContent' -- comma separated if you have multiple properties on your composition
SET @oldCompositionAlias = 'gridContentComposition'
SET @newCompositionAlias = 'newsGridContentComposition'
SET @aliasesToPerformTheSwitchOn = 'newsPage,pressPage'
-- NO TOUCHY FROM HERE - Remember to delete /TEMP folder after run to make Umbraco rebuild caches

--get type ids and build switching table
DECLARE @oldPropertyType AS TABLE(id INT, alias VARCHAR(1000), data_id INT)
DECLARE @newPropertyType AS TABLE(id INT, alias VARCHAR(1000), data_id INT)
DECLARE @switchingTable as TABLE(id_old INT, id_new INT, data_id_old INT, data_id_new INT)

--get the composition ids
SET @oldCompositionId = (SELECT nodeId FROM cmsContentType WHERE alias = @oldCompositionAlias)
SET @newCompositionId = (SELECT nodeId FROM cmsContentType WHERE alias = @newCompositionAlias)
--get list of property types, ids and data-type-ids
INSERT INTO @oldPropertyType SELECT id, Alias, dataTypeId AS data_id FROM cmsPropertyType WHERE Alias IN (SELECT value FROM STRING_SPLIT(@propertyTypeAlias,',')) AND contentTypeId = @oldCompositionId
INSERT INTO @newPropertyType SELECT id, Alias, dataTypeId AS data_id  FROM cmsPropertyType WHERE Alias IN (SELECT value FROM STRING_SPLIT(@propertyTypeAlias,',')) AND contentTypeId = @newCompositionId
--build into a table we can iterate over
INSERT INTO @switchingTable 
	SELECT o.id AS id_old, n.id AS id_new, o.data_id AS data_id_old, n.data_id AS data_id_new FROM @oldPropertyType o
	LEFT JOIN @newPropertyType n on o.alias = n.alias
--DTO vars for the cursor we'll be using to iterate
DECLARE @oldPropId AS INT, @newPropId AS INT, @oldDataId AS INT, @newDataId AS INT
DECLARE @tmpConfig as VARCHAR(max)

DECLARE propCursor CURSOR
	LOCAL STATIC READ_ONLY FORWARD_ONLY
	FOR SELECT * FROM @switchingTable

OPEN propCursor
WHILE 1 = 1 --prevent double fetch
BEGIN
	FETCH NEXT FROM propCursor into @oldPropId, @newPropId, @oldDataId, @newDataId
	-- break loop if done 
	IF @@fetch_status <> 0
	BEGIN
		BREAK
	END
	-- else update next set of properties
	UPDATE umbracoPropertyData 
	SET propertyTypeId = @newPropId 
	WHERE id IN (
		SELECT upd.id FROM umbracoContent uc, umbracoContentVersion ucv, umbracoPropertyData upd
		WHERE contentTypeId IN (SELECT nodeId FROM cmsContentType WHERE alias IN (SELECT value FROM STRING_SPLIT(@aliasesToPerformTheSwitchOn,',')))
		AND ucv.nodeId = uc.nodeId
		AND upd.versionId = ucv.id
		AND upd.propertyTypeId = @oldPropId 
	)
	--update configs on data-types to be exatch match, you'll have to adjust in Umbraco after
	SET @tmpConfig = (SELECT config from umbracoDataType WHERE nodeId = @oldDataId)
	UPDATE umbracoDataType SET config = @tmpConfig WHERE nodeId = @newDataId

END
CLOSE propCursor
DEALLOCATE propCursor

--update relation of contenttypes (actual switching of composition, the rest was just to make the data match)
UPDATE cmsContentType2ContentType 
	SET parentContentTypeId = @newCompositionId 
	WHERE parentContentTypeId = @oldCompositionId 
	AND childContentTypeId IN (SELECT nodeId FROM cmsContentType WHERE alias IN (SELECT value FROM STRING_SPLIT(@aliasesToPerformTheSwitchOn,',')))
--done