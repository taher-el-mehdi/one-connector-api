# Field mapping

Mappings are centralized in `Domain/Mapping/EntityFieldMaps.cs`. Do not duplicate them in controllers.

## Supplier (`F_COMPTET` ↔ Fournisseur)

| DocuWare | Sage | Type |
|---|---|---|
| NUM | CT_Num | Text (key) |
| INTITULE | CT_Intitule | Text |
| TYPE | CT_Type | Numeric (not written back to Sage) |
| CG_NUMPRINC | CG_NumPrinc | Numeric (string on Sage write-back) |
| QUALITE | CT_Qualite | Text |
| CLASSEMENT | CT_Classement | Text |
| CONTACT | CT_Contact | Text |
| ADRESSE | CT_Adresse | Text |
| COMPLEMENT | CT_Complement | Text |
| CODEPOSTAL | CT_CodePostal | Text |
| VILLE | CT_Ville | Text |
| PAYS | CT_Pays | Text |
| APE | CT_Ape | Text |
| IDENTIFIANT | CT_Identifiant | Text |
| STATISTIQUE01 | CT_Statistique01 | Text |
| EMAIL | CT_EMail | Text |
| TELEPHONE | CT_Telephone | Text |
| CA_NUM | CA_Num | Text |
| CBMODIFICATION | cbModification | DateTime (read-only on reverse) |
| CBCREATION | cbCreation | DateTime (read-only on reverse) |

Empty/null Sage values are omitted from DocuWare payloads.

## Chart of accounts (`F_COMPTEG` ↔ Plan Comptable)

`CG_NUM`, `CG_INTITULE`, `N_NATURE`, computed `NUMINTITULE` (`CG_Num-CG_Intitule`), `CBMODIFICATION`, `CBCREATION`.

## Analytic section (`F_COMPTEA` ↔ Section analytique)

`CODE` ← `CA_Num`, `DESCRIPTION` ← `CA_Intitule`, plus creation/modification dates.
