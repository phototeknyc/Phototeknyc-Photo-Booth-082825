# Template ID Mapping Issue and Solution

## The Problem
When templates are deleted and re-synced from the cloud, they get new auto-incremented IDs instead of preserving their original IDs. This breaks event-template associations.

## Current Situation
- Templates have been re-imported with new IDs (8-13 instead of original 2-7)
- Events still reference the old template IDs
- The sync process needs to maintain ID consistency

## Immediate Fix (Run in C# Interactive)
```csharp
// Load the fix script
#load "FixEventTemplateAssociations.cs"
```

## Long-term Solution Options

### Option 1: Disable AUTOINCREMENT (Recommended)
Modify the Templates table to not use AUTOINCREMENT, allowing us to set specific IDs:

```sql
-- Create new table without AUTOINCREMENT
CREATE TABLE Templates_New (
    Id INTEGER PRIMARY KEY,  -- No AUTOINCREMENT
    Name TEXT NOT NULL,
    -- ... other columns
);

-- Copy data
INSERT INTO Templates_New SELECT * FROM Templates;

-- Replace old table
DROP TABLE Templates;
ALTER TABLE Templates_New RENAME TO Templates;
```

### Option 2: Template Name-Based Matching
Instead of relying on IDs, events should reference templates by name:
- Events store TemplateName (already doing this)
- At runtime, resolve template by name to get current ID
- More flexible but requires lookup each time

### Option 3: GUID-Based IDs
Use GUIDs instead of auto-increment integers:
- Templates get permanent GUIDs that never change
- No conflicts when syncing between machines
- Requires schema change

## Recommended Approach
1. **Immediate**: Run the fix script to repair current associations
2. **Short-term**: Update sync to use SaveTemplateWithId properly
3. **Long-term**: Consider switching to GUID-based IDs or name-based resolution

## Testing After Fix
1. Run the fix script
2. Check events - templates should be associated correctly
3. Sync to cloud to save corrected associations
4. Test on another machine to verify sync works