# DBT SharePoint Schema (v1)

## Core lists
- `DBT_Counters`: atomic counters per counter key (Prefix+Date)
  - DateKey (Text, required, unique)  # key like PREFIX_DDMMYY
  - Prefix (Text, required)
  - NextNumber (Number, required, integer)  # next number to issue (starts at 0)

- `DBT_Productions`: production catalog/log
  - VolumeLabel (Text, required, unique)
  - Prefix (Text, required)
  - DateKey (Text, required)
  - Sequence (Number, required, integer)
  - Status (Choice: Reserved/InProgress/Completed/Cancelled)
  - ReservedAt (DateTime)
  - ReservedBy (Person)

## Lookups
- `DBT_Lookup_EmployeeAbbrev`
  - Abbrev (Title) (Text, required, unique, validation: exactly 3 uppercase chars)
  - FullName (Text)

- `DBT_Lookup_ProducedFor`
  - InternalTitle (Title) (Text, optional)
  - Code (Text, required, unique, max 5, recommended 3–5 uppercase)
  - OrganizationName (Text, required)
  - AddressLine1, PostalCode, City, Country (Text, required)
  - Department, Attention, AddressLine2, StateOrRegion, Email, Phone, Notes (Text)

- `DBT_Lookup_ProducedFrom` (same as ProducedFor, without Code)
- `DBT_Lookup_ReturnAddress` (same as ProducedFor, without Code)

## Important: SharePoint Title column
SharePoint always has a `Title` column which cannot be deleted.
This schema keeps it as `InternalTitle` on address lists and makes it optional.
