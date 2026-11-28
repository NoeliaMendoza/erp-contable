# ACC-04 · Diseño de configuración contable por empresa (#36)

**Autor:** Romero (joel1251) · **Fecha:** 28/11/2026 · **Estado del documento:** propuesta de diseño

> **Este documento es diseño, no implementación.** No se ha escrito código de #36.
> Cada apartado marca explícitamente qué **EXISTE** hoy en la rama y qué está **PROPUESTO**.
>
> **Requisito de arranque:** #36 no empieza su implementación hasta que **#17 esté integrada en `main`**.
> La rama de #17 combina `origin/main` (`4b82bf9`) con el trabajo contable y se presenta en un PR en
> borrador: sigue pendiente resolver la incompatibilidad de Ventas y la aceptación de Noe. Este archivo
> es el único entregable de #36 por ahora; no declara implementada esa tarea.

## 1. Punto de partida comprobado

| Hecho | Estado |
|---|---|
| Plan de cuentas por empresa (`accounting.accounts`, RLS, `IsActive`, `AllowsPosting`) | **EXISTE** (#6 / ACC-01, aceptada) |
| Línea de asiento con `AccountId` (FK compuesta) **y** `AccountCode` del momento de contabilizar | **EXISTE** (#17) |
| Asientos y líneas inmutables, con trazabilidad al documento origen | **EXISTE** (#17) |
| `IAccountingPostingService.PostAsync` exige **códigos** del plan de la empresa activa | **EXISTE** (#17) |
| Error `posting_account_not_available` (422) cuando una cuenta no existe, está inactiva o no admite movimientos | **EXISTE** (#17) |
| Compras resuelve sus cuentas desde `Purchases:PostingAccounts:<tenantId>` (`Inventory`, `Cash`) | **EXISTE** (#47, provisional) |
| Ventas envía nombres fijos («Caja», «Ventas», «IVA por pagar», «Costo de ventas», «Inventario») | **EXISTE y falla** con `posting_account_not_available` |
| Tabla/servicio de configuración de cuentas por función | **PROPUESTO** (este documento) |
| Períodos contables, asientos manuales, balance de comprobación | **PROPUESTO**, fuera de esta etapa (§8) |

El contrato ACUERDO-02 ya fija la regla que este diseño implementa
(docs/contratos-modulos.md, «Cuentas y responsabilidad de cada operación»):
*«Si falta una cuenta configurada, la operación se rechaza; no se crean cuentas ni se escogen otras automáticamente.»*

## 2. Configuración por empresa — funciones contables (PROPUESTO)

Una **función contable** es el papel que cumple una cuenta en una operación. La configuración asocia, por empresa,
cada función a **un código del plan de esa empresa**. No hay códigos universales entre empresas.

| Función (clave estable) | Uso | Naturaleza esperada | Requerida cuando |
|---|---|---|---|
| `Cash` | Caja | Activo / Debe | Compra o venta al contado |
| `Revenue` | Ventas (ingreso) | Ingreso / Haber | Toda venta |
| `Inventory` | Inventario | Activo / Debe | Operación con productos que llevan inventario |
| `CostOfGoodsSold` | Costo de ventas | Gasto / Debe | Venta con costo de mercancía > 0 |
| `VatPurchases` | IVA compras (recuperable) | Activo / Debe | Compra con IVA y derecho a crédito tributario |
| `VatPayable` | IVA por pagar | Pasivo / Haber | Venta con IVA |
| `AccountsReceivable` | Cuentas por cobrar | Activo / Debe | Venta a crédito |
| `AccountsPayable` | Cuentas por pagar | Pasivo / Haber | Compra a crédito |

Decisiones de diseño:

- La clave es un **enum cerrado** (`PostingAccountFunction`), no texto libre: evita que cada módulo invente nombres.
- Las ocho funciones se **configuran**; sólo se **exigen** las que la operación concreta necesita. Una venta de
  servicios sin IVA no requiere `Inventory`, `CostOfGoodsSold` ni `VatPayable`.
- La naturaleza esperada se **advierte**, no se impone: el plan de cada empresa es suyo. Se registra como aviso en
  la pantalla, no como rechazo, salvo que el equipo decida lo contrario (decisión abierta D-3, §9).

### 2.1 Persistencia (PROPUESTO — sin migración en esta etapa)

```
accounting.posting_account_map
  tenant_id   uuid    not null
  function    text    not null   -- valor del enum, p. ej. 'Cash'
  account_id  uuid    not null   -- FK compuesta (tenant_id, account_id) -> accounting.accounts
  updated_at_utc timestamptz not null
  updated_by  uuid    null
  primary key (tenant_id, function)
```

- **FK compuesta `(tenant_id, account_id)`**, igual que ya hace el plan de cuentas con el padre: impide configurar
  una cuenta de otra empresa incluso con contexto correcto.
- **RLS forzado** con `RlsSql.EnableTenantIsolation` y grants, en la **misma** migración que cree la tabla.
- Se guarda `account_id`, no el código: si la empresa renombra o reordena códigos, el mapeo no se rompe.
  El código se resuelve al leer (§3).
- **La migración se genera después**, sobre `main` ya con #17 integrada, según docs/contribucion-git.md §Migraciones.
  No se genera ahora para no colisionar con el `AccountingDbContext` de #17.

## 3. Contrato mínimo en `Accounting.Contracts` (PROPUESTO — pendiente de acuerdo)

> **Nada de este apartado está acordado todavía.** Los nombres del enum, las firmas y el código de error son una
> propuesta mía que **requiere el visto bueno de Michael (Ventas) y Torres (Compras)** antes de escribirse como
> código: son ellos quienes van a consumirla. Mientras no lo confirmen, estas firmas pueden cambiar.
> Las decisiones concretas que les pido están en §9.

Objetivo: que Ventas y Compras pregunten «¿qué código uso para Caja en esta empresa?» sin conocer tablas de
Contabilidad y sin poder elegir la empresa.

```csharp
namespace ERP.Modules.Accounting.Contracts;

/// <summary>Papel de una cuenta en una operación. Enum cerrado: los módulos no inventan claves.</summary>
public enum PostingAccountFunction
{
    Cash,
    Revenue,
    Inventory,
    CostOfGoodsSold,
    VatPurchases,
    VatPayable,
    AccountsReceivable,
    AccountsPayable,
}

/// <summary>
/// Códigos resueltos para una operación, ya validados contra el plan de la empresa activa.
/// Sólo contiene las funciones solicitadas.
/// </summary>
public sealed class PostingAccountSet
{
    /// <summary>Código del plan para esa función. Lanza si no se solicitó al resolver.</summary>
    public string this[PostingAccountFunction function] { get; }

    public bool Contains(PostingAccountFunction function);
}

/// <summary>
/// Consulta de la configuración de cuentas de la empresa activa. La empresa se toma del
/// <c>ITenantContext</c> confiable del servidor: ningún parámetro la recibe.
/// </summary>
public interface IPostingAccountMap
{
    /// <summary>
    /// Resuelve y valida todas las funciones pedidas en una sola consulta.
    /// Si falta alguna, o su cuenta está inactiva o no admite movimientos, lanza
    /// <c>DomainException("posting_account_not_configured")</c> (422) nombrando las funciones que faltan,
    /// sin revelar el plan de otra empresa. No crea cuentas ni elige por nombre.
    /// </summary>
    Task<PostingAccountSet> ResolveAsync(
        IReadOnlyCollection<PostingAccountFunction> functions,
        CancellationToken cancellationToken);

    /// <summary>Lectura para la pantalla de configuración; incluye funciones sin configurar.</summary>
    Task<IReadOnlyList<PostingAccountAssignment>> GetAllAsync(CancellationToken cancellationToken);
}

/// <param name="Function">Función contable.</param>
/// <param name="AccountCode">Código configurado, o null si la función no está configurada.</param>
/// <param name="AccountName">Nombre de la cuenta, para mostrar.</param>
/// <param name="IsUsable">true si la cuenta está activa y admite movimientos hoy.</param>
public sealed record PostingAccountAssignment(
    PostingAccountFunction Function,
    string? AccountCode,
    string? AccountName,
    bool IsUsable);
```

Notas de contrato:

- `ResolveAsync` recibe **el conjunto completo** que la operación necesita y falla **una sola vez** con la lista de
  funciones faltantes. Evita que Ventas descubra los problemas de a uno.
- `Contracts` sigue sin depender de EF, Npgsql ni ASP.NET, como el resto de ACUERDO-02.
- El error es **`posting_account_not_configured`** (nuevo, 422) y se distingue de
  `posting_account_not_available` (#17, cuenta pedida por código que no sirve). Dos causas, dos códigos:
  «no me dijiste qué cuenta usar» frente a «la cuenta que indicaste no sirve».

### 3.1 Ejemplo de uso desde Ventas (PROPUESTO)

```csharp
// 0) PRIMERO el reintento: si la venta ya está confirmada, devolver su resultado y NO consultar el mapeo.
//    Un cambio de configuración posterior no debe alterar este camino (§4.5).
var existing = await db.Sales.Include(s => s.Lines)
    .SingleOrDefaultAsync(s => s.IdempotencyKey == request.IdempotencyKey.Trim(), ct);
if (existing is not null)
    return ToResponse(existing);   // conserva JournalEntryId y StockMovementId originales

// 1) Documento nuevo: resolver y validar las cuentas ANTES de mover inventario.
var functions = new List<PostingAccountFunction> { PostingAccountFunction.Cash, PostingAccountFunction.Revenue };
if (taxTotal > 0)
    functions.Add(PostingAccountFunction.VatPayable);
if (hasInventoryLines)
{
    functions.Add(PostingAccountFunction.CostOfGoodsSold);
    functions.Add(PostingAccountFunction.Inventory);
}

var accounts = await postingAccounts.ResolveAsync(functions, ct);   // 422 aquí si falta configuración

// 2) Recién ahora se mueve el stock, que ya no habrá que deshacer por un problema de configuración.
var stockResult = await inventory.RegisterAsync(/* ... */, ct);

// 3) Armar el asiento con códigos, nunca con nombres.
var lines = new List<PostingLine>
{
    new(accounts[PostingAccountFunction.Cash], sale.Total, 0m, "Caja por venta al contado"),
    new(accounts[PostingAccountFunction.Revenue], 0m, subtotal, "Ingresos de venta"),
};
if (taxTotal > 0)
    lines.Add(new PostingLine(accounts[PostingAccountFunction.VatPayable], 0m, taxTotal, "IVA calculado"));
if (stockResult.TotalCost > 0)
{
    lines.Add(new PostingLine(accounts[PostingAccountFunction.CostOfGoodsSold], stockResult.TotalCost, 0m, "Costo de mercancía vendida"));
    lines.Add(new PostingLine(accounts[PostingAccountFunction.Inventory], 0m, stockResult.TotalCost, "Salida de inventario"));
}

var posting = await accounting.PostAsync(new PostingRequest(/* ... */, lines), ct);
```

Ventas no recibe ni envía `tenantId`: lo toma el servidor del `ITenantContext`.

## 4. Validaciones (PROPUESTO)

### 4.1 Al guardar la configuración

1. La cuenta **pertenece a la empresa activa** — garantizado por FK compuesta + RLS, no sólo por código C#.
2. La cuenta está **activa** (`IsActive`) y **admite movimientos** (`AllowsPosting`). Una cuenta de agrupación
   (`AllowsPosting = false`) se rechaza: `DomainException("posting_account_not_postable", 422)`.
3. La cuenta se indica por **código**, se normaliza con `Account.NormalizeCode` (igual que ACC-01) y debe existir.
   Un nombre con espacios no es un código: se rechaza, **no se busca por nombre**.
4. **Nunca se crea una cuenta automáticamente.** Si no existe, el usuario la crea primero en el plan de cuentas.
5. Se permite que dos funciones apunten a la misma cuenta sólo si es contablemente admisible; el caso
   «misma cuenta en debe y haber de la misma operación» lo rechaza el consumidor, no el mapeo
   (Compras ya exige `Inventory ≠ Cash`). Decisión abierta D-2 (§9).
6. Requiere permiso de escritura (§4.6).

### 4.2 Al resolver para una operación

- `ResolveAsync` revalida **en el momento de usar**: la configuración pudo quedar obsoleta.
- Falta la función → `posting_account_not_configured` (422), nombrando las funciones faltantes.
- Cuenta configurada pero inactiva o sin movimientos → mismo error 422, indicando que debe corregirse la
  configuración. **No se sustituye por otra cuenta.**
- `ResolveAsync` se llama **antes de `IInventoryMovementService.RegisterAsync`**, y sólo cuando el documento
  **no** estaba ya confirmado (§4.5).

> **Validar antes de mover inventario no sustituye la transacción compartida de #35.**
> Resolver la configuración primero evita *una* causa de inconsistencia —la más frecuente y la más barata de
> evitar— pero no aporta atomicidad. Si el movimiento de stock se registra y después falla el asiento por
> cualquier otro motivo (período cerrado, conflicto de idempotencia, caída de la conexión), **sigue haciendo
> falta que documento, movimiento y asiento compartan una sola conexión y transacción PostgreSQL**.
> Hoy eso sólo está demostrado en Compras, mediante `InSharedTenantTransactionAsync`; Ventas abre todavía su
> transacción local. Ese trabajo es **#35, de Reyes**, y no lo cubre ni lo reemplaza este diseño.
> Este documento no debe citarse como evidencia de rollback entre módulos.

### 4.3 Desactivar o cambiar una cuenta ya configurada

| Acción | Comportamiento propuesto |
|---|---|
| Desactivar (o quitar `AllowsPosting`) una cuenta **configurada** | **Se permite**, con **aviso explícito** en la pantalla del plan de cuentas indicando qué funciones quedan inutilizables. No se bloquea: cerrar una cuenta es una decisión contable legítima. |
| Operar después de esa desactivación | **Se rechaza** con 422 en `ResolveAsync`. La operación no se «arregla» sola. |
| Cambiar el mapeo de una función a otra cuenta | **Se permite** y aplica **sólo a asientos futuros**. |
| Asientos ya contabilizados con la cuenta anterior | **No se tocan.** Siguen apuntando a su cuenta: los asientos son inmutables (#17) y la historia no se reescribe. |
| Reintento idempotente de un documento contabilizado antes del cambio | Devuelve el **asiento original**, con las cuentas antiguas, **sin volver a consultar el mapeo** (§4.5). Es la regla que ya cumple #17: misma clave y mismo contenido devuelve el asiento existente. |

Cada cambio de mapeo deja `updated_at_utc` / `updated_by`. Un historial completo de versiones del mapeo queda
fuera de #36 (decisión abierta D-4, §9).

### 4.4 Conservación de los códigos y datos contables del documento (EXISTE en #17)

Un cambio de configuración **no puede reescribir la contabilidad ya registrada**. Eso no depende de #36: el
mecanismo ya está en la rama y #36 se limita a no romperlo.

- `JournalEntryLineEntity` guarda **dos** cosas por línea: `AccountId`, clave foránea compuesta
  `(tenant_id, account_id)` hacia el plan, y **`AccountCode`, el código vigente en el momento de contabilizar**.
  La propia documentación del tipo lo dice: el código se conserva «para que el libro diario no dependa de una
  lectura posterior del plan».
- Por tanto el libro diario y el futuro balance de comprobación muestran **el código con el que se contabilizó**,
  no el que esté configurado hoy. Si la empresa reasigna la función `Cash` a otra cuenta, los asientos anteriores
  siguen mostrando la suya.
- `JournalEntryEntity` conserva además la trazabilidad completa al documento origen: `SourceModule`,
  `SourceDocumentType`, `SourceDocumentId`, `AccountingDate`, `Description` e `IdempotencyKey`.
- Los asientos y sus líneas son **inmutables**: no se editan ni se eliminan
  (hay prueba de integración, `JournalImmutabilityTests`). Una corrección futura será un **reverso** auditable,
  que es **#34** y sigue fuera de #36.
- **El mapeo de #36 es una tabla de decisión, no una fuente de verdad histórica.** Nunca se consulta para
  *leer* contabilidad pasada; sólo para *construir* un asiento nuevo.

### 4.5 Reintento de una operación ya confirmada (orden obligatorio)

Este es el punto donde un consumidor puede equivocarse y corromper un documento ya contabilizado. El orden
**no** es negociable:

1. **Primero**, buscar el documento por su clave de idempotencia. Si ya está confirmado, **devolver el resultado
   existente** (su `JournalEntryId` y su `StockMovementId`) y terminar.
2. Si la respuesta anterior quedó incierta y hay que repetir la contabilización, **reutilizar la solicitud
   original** —las mismas líneas y los mismos códigos ya guardados— con la **misma** `IdempotencyKey`.
3. **Sólo si el documento no estaba confirmado** se consulta el mapeo vigente con `ResolveAsync` y se arma un
   asiento nuevo.

Consecuencias:

- Un reintento **nunca** consulta el mapeo vigente. Da igual que entre el primer intento y el reintento se haya
  desactivado o reasignado una cuenta configurada: **el reintento no cambia**.
- Esto es lo que impide el fallo más peligroso: que repetir una operación ya confirmada genere un asiento con
  cuentas distintas, o que un cambio de configuración haga fallar el reintento de algo que ya estaba bien.
- `PostAsync` ya respalda este orden en #17: busca el asiento por clave **antes** de validar cuentas, de modo que
  *«un asiento ya confirmado se devuelve aunque después se desactive una cuenta, pero eso no autoriza un asiento
  nuevo»*. Compras ya lo cumple: `ConfirmAsync` devuelve `ToResponse(purchase)` si el estado es `Confirmed`,
  antes de leer su configuración de cuentas.
- Lo mismo aplicará a `period_closed` cuando exista (entrega 2 de §8): cerrar un período no debe romper el
  reintento de un documento contabilizado mientras estaba abierto.

### 4.6 Permisos

- Lectura de la pantalla: `accounting.accounts.read` (**EXISTE**).
- Edición del mapeo: **PROPUESTO** `accounting.posting-map.write`, permiso propio y no `accounting.accounts.write`:
  configurar el mapeo afecta a Ventas y Compras, no sólo al plan.
- `IPostingAccountMap.ResolveAsync` es consumo **servidor a servidor** dentro de la petición del módulo origen:
  no exige permiso de contabilidad al vendedor. Lo que se autoriza es la venta.
- Aislamiento entre empresas por RLS, como el resto del módulo.

## 5. Pantalla de configuración (PROPUESTO, boceto)

Nueva entrada de menú **«Cuentas por operación»** en `ShellViewModel`, junto a «Plan de cuentas» y «Libro diario»,
visible con la característica `accounting`. Reutiliza los componentes que ya existen en `AccountsView.axaml`:
`controls:FeedbackBar`, `controls:FormField`, `Classes="card"`, `h1`/`h2`/`muted`.
No necesita `PaginationBar`: son ocho filas fijas.

```
┌─ Cuentas por operación ───────────────────────────── (h1) ─┐
│ [FeedbackBar: IsBusy / ErrorMessage / SuccessMessage]      │
│                                                            │
│ Empresa activa: Comercial Andina        (muted small)      │
│ Indique qué cuenta del plan cumple cada función.  (muted)  │
│                                                            │
│ ┌─ card ──────────────────────────────────────────────────┐│
│ │ Función             Cuenta                     Estado   ││
│ │ Caja                [1.1.01 Caja general  ▼]   ✔        ││
│ │ Ventas              [4.1.01 Ventas        ▼]   ✔        ││
│ │ Inventario          [1.1.02 Inventario    ▼]   ✔        ││
│ │ Costo de ventas     [5.1.01 Costo ventas  ▼]   ✔        ││
│ │ IVA compras         [— sin configurar —   ▼]   ⚠ falta  ││
│ │ IVA por pagar       [2.1.05 IVA por pagar ▼]   ✔        ││
│ │ Cuentas por cobrar  [— sin configurar —   ▼]   ⚠ falta  ││
│ │ Cuentas por pagar   [2.1.01 Proveedores   ▼]   ✘ inactiva││
│ └─────────────────────────────────────────────────────────┘│
│                                     [ Guardar ] (primary)  │
│ ⚠ «IVA por pagar» sin configurar impide confirmar ventas   │
│   con IVA.                                  (muted small)  │
└────────────────────────────────────────────────────────────┘
```

- Cada fila es un `controls:FormField` con un `ComboBox` que lista **sólo** cuentas de la empresa activa con
  `IsActive && AllowsPosting`, más la opción «— sin configurar —».
- La columna Estado distingue tres casos: configurada y usable (✔), sin configurar (⚠) y configurada pero
  inutilizable (✘), que es el caso de §4.3 y debe ser visible sin abrir el plan de cuentas.
- El aviso inferior explica la consecuencia operativa, no sólo que falta un dato.
- Endpoints **PROPUESTOS**: `GET /api/v1/accounting/posting-accounts` y `PUT /api/v1/accounting/posting-accounts`,
  siguiendo las convenciones CRUD v1 de docs/contratos-modulos.md.

## 6. Sustitución del mapeo provisional (PROPUESTO — no se toca nada ahora)

**En esta etapa no se modifica ningún servicio ajeno.** Orden propuesto, cada paso en su propio PR:

| Paso | Quién | Qué | Precondición |
|---|---|---|---|
| 0 | Romero | #17 integrada en `main` | PR de #17 aprobado |
| 1 | Romero | Tabla + migración + `IPostingAccountMap` + pantalla + endpoints | Paso 0 |
| 2 | Michael (Ventas) | Reemplazar los cinco nombres fijos por `ResolveAsync`; resolver **antes** de mover inventario y **después** de descartar el reintento (§4.5) | Paso 1 |
| 3 | Torres (Compras) | Reemplazar `Purchases:PostingAccounts:<tenantId>` por `ResolveAsync`; borrar la sección de configuración y su uso en `PurchasesApiTests` | Paso 1 |
| 4 | Romero | Quitar del contrato la mención al mapeo provisional y actualizar docs/contratos-modulos.md | Pasos 2 y 3 |

Reglas de la transición:

- Los pasos 2 y 3 son **independientes**: Compras puede quedarse con `IConfiguration` mientras Ventas ya usa el
  servicio, y al revés. No se exige un cambio simultáneo.
- Ventas está **bloqueada hoy** y no puede esperar al paso 1. Mientras tanto existe la propuesta intermedia de
  `Sales:PostingAccounts:<tenantId>` (mismo patrón que Compras), que el paso 2 después retira. Es deuda
  reconocida, no la solución final.
- **No se relaja la validación de #17** en ningún paso. Ventas falla hasta que envíe códigos reales.
- El paso 2 **no** incluye la transacción compartida de Ventas: eso es **#35 de Reyes** y va por separado.

## 7. Plan de pruebas (PROPUESTO)

Unitarias (`tests/ERP.UnitTests/Accounting`):

1. `PostingAccountSet` indexador: función solicitada devuelve código; función no solicitada lanza.
2. Normalización del código al guardar (`Account.NormalizeCode`); un nombre con espacios se rechaza.
3. `ResolveAsync` con lista vacía: no consulta la base y no falla.

Integración (`tests/ERP.IntegrationTests`, PostgreSQL real por Testcontainers):

4. **Configuración ausente**: `ResolveAsync` de una función sin configurar → 422
   `posting_account_not_configured`, y el mensaje nombra la función que falta.
5. **Falta más de una**: el error las lista todas en una sola respuesta.
6. **Cuenta inactiva**: se configura, luego se desactiva → 422 al resolver; la configuración no se borra sola.
7. **Cuenta sin movimientos** (`AllowsPosting = false`): se rechaza **al guardar** el mapeo.
8. **Cuenta de otra empresa**: configurarla se rechaza aunque el contexto de empresa sea correcto (FK compuesta).
9. **Aislamiento entre empresas**: la misma función apunta a códigos distintos en dos empresas y cada una lee el
   suyo; sin contexto de empresa no se lee ninguna fila (patrón de `AccountingRlsTests`).
10. **Cambio de mapeo**: asientos anteriores conservan su cuenta; el reintento idempotente del documento anterior
    devuelve el asiento original con las cuentas antiguas.
11. **El reintento no consulta el mapeo**: confirmar un documento, **reasignar** la función a otra cuenta y
    reintentar → devuelve el mismo `JournalEntryId` y las líneas conservan el `AccountCode` original,
    no el nuevo.
12. **El reintento sobrevive a la desactivación**: confirmar un documento, **desactivar** la cuenta configurada
    y reintentar → **no** da 422; devuelve el resultado existente. Un documento nuevo en esas condiciones sí
    se rechaza.
13. **Código histórico en el libro diario**: tras reasignar la función, el libro diario del asiento anterior
    sigue mostrando el `AccountCode` con el que se contabilizó.
14. **Permisos**: lectura no autoriza escritura del mapeo; sin sesión o con empresa ajena se rechaza.
15. **Consumo desde Ventas**: con el mapeo completo, la venta 20 con costo 12 se contabiliza en cuatro líneas
    (recorrido del ACUERDO-02); sin `VatPayable`, la venta con IVA del 15 % se rechaza con 422.
16. **Consumo desde Compras**: la compra 30 se contabiliza con las cuentas del mapeo y el borrador sigue
    revirtiendo cuando falla el asiento (conserva la garantía ya probada en `PurchasesApiTests`).
17. **Orden de resolución**: si falta configuración, **no** se registra movimiento de inventario.

Las pruebas 15 a 17 dependen cada una del paso 2/3 de §6; se escriben con el PR que las habilita, no antes.

## 8. Reparto del resto de #36 (no se implementa ahora)

#36 se divide en cuatro entregas independientes, en este orden:

| Orden | Entrega | Alcance | Depende de |
|---|---|---|---|
| 1 | **Configuración contable** (este documento) | §2–§7 | #17 en `main` |
| 2 | **Períodos contables** (ACC-02) | Catálogo de períodos por empresa, abrir/cerrar/bloquear, consulta «¿período abierto?». Habilita el `period_closed` que #17 **declara pero no evalúa**. Requiere acordar la política antes de codificar: no se presume abierto por ausencia de catálogo. | Entrega 1 |
| 3 | **Asientos manuales** | Alta de asientos desde la pantalla, con las mismas validaciones de #17 (partida doble, cuentas del plan, inmutabilidad) y una clave de idempotencia propia del asiento manual. Necesita períodos para rechazar fechas cerradas. | Entrega 2 |
| 4 | **Balance de comprobación** (ACC-07) | Sumas y saldos por cuenta y rango de fechas, con paginación en servidor, sobre el libro diario de #17. Sólo lectura. | Entrega 1 |

Las entregas 2 y 4 son paralelizables entre sí. La 3 es la única que necesita la 2.
**#34 (reverso de asientos) sigue siendo tarea aparte** y no entra en #36.

## 9. Decisiones que requieren confirmación del equipo

| # | Decisión | Propuesta |
|---|---|---|
| D-1 | ¿Una sección de configuración por módulo o un mapeo único en Contabilidad? | **Mapeo único** en Contabilidad; los módulos sólo consultan. |
| D-2 | ¿Puede una misma cuenta cumplir dos funciones? | Permitirlo en el mapeo; que cada consumidor exija lo que su asiento necesita. |
| D-3 | ¿Validar la naturaleza de la cuenta (`AccountKind`/`NormalBalance`) contra la función? | **Avisar, no rechazar**: el plan es de cada empresa. |
| D-4 | ¿Historial de versiones del mapeo? | Fuera de #36; sólo `updated_at_utc`/`updated_by`. |
| D-5 | ¿Cuentas por cobrar y por pagar entran ya, sin ventas/compras a crédito implementadas? | Configurarlas desde el inicio y no exigirlas hasta que exista la operación a crédito. |

## 10. Referencias

- docs/contratos-modulos.md — ACUERDO-02 (#12), «Cuentas y responsabilidad de cada operación», «Fallos y transacción».
- docs/modulos/acc03-integracion-ventas.md — por qué los nombres de cuenta de Ventas se rechazan.
- docs/contribucion-git.md — §Migraciones (generar sobre `main` actualizado) y §Archivos compartidos.
