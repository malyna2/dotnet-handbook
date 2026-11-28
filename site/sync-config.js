/* Cross-device progress sync. Leave both empty to switch the feature off (the Aa panel then
   shows no sync row). Fill in the Project URL and the *public* anon / publishable key from
   Supabase → Project Settings → API; both are safe to commit as long as supabase/schema.sql
   has been applied (it locks the table down). Never put a service_role key here. */
window.SYNC_CONFIG = {
  url: "https://hxtblspfofcmpiqzhcnv.supabase.co",
  anonKey: "sb_publishable_JAvHtTzbQ12Syji-gsTP7w_ae6iUjix"
};
