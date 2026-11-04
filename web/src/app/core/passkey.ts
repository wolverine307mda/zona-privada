// WebAuthn: entrar y registrar con la cara o la huella (Face ID en el iPhone, Windows Hello en el PC).
// La API manda las opciones en el formato JSON estándar de WebAuthn y el navegador las convierte (Safari 18.4+, Chrome 129+, Edge, Firefox).
type Api = { parseCreationOptionsFromJSON?: (o: unknown) => PublicKeyCredentialCreationOptions; parseRequestOptionsFromJSON?: (o: unknown) => PublicKeyCredentialRequestOptions };

export function soportaPasskeys(): boolean {
  const pk = (globalThis as { PublicKeyCredential?: Api }).PublicKeyCredential;
  return !!pk && typeof pk.parseCreationOptionsFromJSON === 'function' && typeof pk.parseRequestOptionsFromJSON === 'function' && !!navigator.credentials;
}

export async function crearCredencial(opciones: unknown): Promise<unknown> {
  const pk = (globalThis as { PublicKeyCredential: Api }).PublicKeyCredential;
  const cred = (await navigator.credentials.create({ publicKey: pk.parseCreationOptionsFromJSON!(opciones) })) as (PublicKeyCredential & { toJSON(): unknown }) | null;
  if (!cred) throw new Error('cancelado');
  return cred.toJSON();
}

// ¿puede el navegador ofrecer la llave dentro del campo de usuario (autocompletado), sin ventana aparte?
export async function soportaCondicional(): Promise<boolean> {
  const pk = (globalThis as { PublicKeyCredential?: { isConditionalMediationAvailable?: () => Promise<boolean> } }).PublicKeyCredential;
  try { return !!pk?.isConditionalMediationAvailable && (await pk.isConditionalMediationAvailable()); } catch { return false; }
}

export async function pedirCredencial(opciones: unknown, extra?: { condicional?: boolean; senal?: AbortSignal }): Promise<unknown> {
  const pk = (globalThis as { PublicKeyCredential: Api }).PublicKeyCredential;
  const cred = (await navigator.credentials.get({
    publicKey: pk.parseRequestOptionsFromJSON!(opciones),
    ...(extra?.condicional ? { mediation: 'conditional' as CredentialMediationRequirement } : {}),
    ...(extra?.senal ? { signal: extra.senal } : {}),
  })) as (PublicKeyCredential & { toJSON(): unknown }) | null;
  if (!cred) throw new Error('cancelado');
  return cred.toJSON();
}
