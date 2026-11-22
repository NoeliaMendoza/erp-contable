# Contratos entre módulos (v1)

Regla: un módulo **nunca** lee ni escribe tablas de otro. Usa interfaces de un proyecto `*.Contracts`.
Cambiar un contrato = PR revisado por productor **y** consumidores.

## Platform → todos
- `ITenantContext` (`ERP.BuildingBlocks.Tenancy`): `UserId`, `TenantId`, `Permissions`, `RequireTenantId()`.
- Permisos: cada módulo publica `IPermissionProvider` y constantes `XPermissions`. Códigos `modulo.recurso.accion`.
- Feature codes: `platform`, `accounting`, `sales`, `purchases`, `inventory`, `reporting`.

## Sesión del escritorio (Platform → Frontend, PLAT-01 · Reyes)

Interfaces en `ERP.Frontend.Shared/Session/AuthenticationContracts.cs`. Las pantallas y el cliente HTTP dependen
**solo** de ellas; nadie usa OpenIddict ni guarda tokens por su cuenta.

| Tipo | Uso |
|---|---|
| `IAuthenticationService` | `SignInAsync(ct)` abre el navegador del sistema (Authorization Code + PKCE) y devuelve `SignInResult` (`Succeeded` / `Cancelled` / `Failed` + mensaje para el usuario). `SignOutAsync(ct)` cierra la sesión local. Evento `SessionEnded` (`SignedOut` / `Expired`). |
| `IAccessTokenProvider` | Lo consume `SessionHeadersHandler`: `GetAccessTokenAsync(ct)` devuelve el token vigente (lo renueva una vez si está por expirar) o `null`; `ReportUnauthorized(token)` cuando la API responde 401 → fin de sesión, **sin reintentos**. |
| `SessionState` | Se mantiene: perfil (`/api/v1/me`), empresa seleccionada, permisos y módulos habilitados (solo UX). |

Reglas:
- La API autentica con `Authorization: Bearer <token>`; `X-Tenant-Id` es solo la **selección** de empresa y el servidor
  valida membresía y permiso en cada solicitud (sin membresía → 403).
- Tokens solo en memoria; nunca en registros, archivos, capturas ni Git.
- `X-Dev-User` queda solo para Development/Testing (pruebas y `curl`); el escritorio deja de enviarlo.
- Un módulo nuevo **no** necesita tocar nada de esto: usa `ErpApiClient.GetAsync<T>` / `SendJsonAsync<T>`, que ya
  adjuntan token y empresa.

## Plan de cuentas (ACC-01, Romero)

El CRUD de `/api/v1/accounts` usa los permisos `accounting.accounts.read` y `accounting.accounts.write`.
Empresa y auditoría se obtienen del servidor. El código es texto normalizado y único por empresa,
incluso si la cuenta está inactiva; el padre pertenece a la misma empresa y no puede crear ciclos.
La desactivación conserva las relaciones y no desactiva hijos en cascada.
DTO, errores, migración y demostración: [modulos/contabilidad.md](modulos/contabilidad.md).
Este mantenimiento no implementa todavía el servicio de contabilización del siguiente apartado.

## Accounting.Contracts — ACUERDO-02 (#12)

**Estado: base v1 e IVA aprobados por Noe e integrados en los PR #25 y #26; tarea #12 cerrada por Noe.**
Las firmas públicas existentes se conservan. La revisión y coordinación del productor con los consumidores
se mantiene para cambios posteriores; esta definición no declara implementado `IAccountingPostingService` ni completa ACC-03 (#17).
El estado de cada decisión y la revisión requerida por Noe, Compras y Ventas constan en [modulos/acuerdo02-propuesta.md](modulos/acuerdo02-propuesta.md).

### Entrada y resultado

Los módulos llaman a `IAccountingPostingService.PostAsync(PostingRequest, CancellationToken)`
dentro del servidor. No escriben directamente en tablas contables ni agregan un endpoint público de contabilización.

| Dato | Contrato |
|---|---|
| Empresa | `ITenantContext.RequireTenantId()` de la solicitud autenticada y autorizada; no se añade un TenantId editable al DTO |
| `IdempotencyKey` | Texto estable de la operación, generado a partir del módulo, tipo e ID persistente del documento |
| `SourceModule` | `purchases` o `sales` en este incremento; debe corresponder al módulo que confirma el documento |
| `SourceDocumentType` | `receipt` para la compra recibida; `invoice` para la venta del ejemplo |
| `SourceDocumentId` | UUID no vacío del documento de la empresa activa; el módulo propietario verifica su pertenencia |
| `AccountingDate` | `DateOnly` de la operación en Ecuador; no se sustituye por la fecha UTC del servidor |
| `Description` | Descripción no vacía; sin credenciales ni datos personales innecesarios |
| `Lines` | Al menos dos `PostingLine(AccountCode, Debit, Credit, Memo)`; Memo es opcional |

La empresa y el usuario de auditoría se obtienen del contexto confiable. Cada cuenta se resuelve por **código**
normalizado dentro de esa empresa y debe existir, estar activa y permitir movimientos. Una cuenta ajena,
inexistente, inactiva o no apta se rechaza sin revelar datos de otra empresa.

Cada línea usa `decimal` en USD, con debe XOR haber positivo, sin negativos y como máximo dos decimales.
La propuesta conserva la escala provisional existente de `AccountingMoney` (D-05); el servicio no redondea
ni agrega diferencias para cuadrar. El módulo origen debe enviar importes finales ya calculados.
La suma del debe debe ser exactamente igual a la del haber y mayor que cero. D-05 no se considera cerrada
para impuestos, descuentos, moneda extranjera ni reglas de redondeo adicionales.

El resultado es `PostingResult(JournalEntryId, AlreadyProcessed)`:

- Primera solicitud: ID del asiento persistido, `AlreadyProcessed = false`.
- Reintento idéntico: mismo ID, `AlreadyProcessed = true`; no se crean más asientos ni líneas.
- El resultado no equivale al commit del caso de uso: solo se comunica éxito al cliente después del commit global.

### Cuentas y responsabilidad de cada operación

| Operación | Quién solicita | Debe | Haber | Importe didáctico |
|---|---|---|---|---|
| Compra recibida al contado | Compras | Inventario | Caja | 30 |
| Ingreso de venta al contado | Ventas | Caja | Ventas | 20 |
| Costo de los productos vendidos | Ventas, con costo calculado por Inventario | Costo de ventas | Inventario | 12 |

**Propuesta concreta para revisar:** en el ejemplo sin IVA, una compra genera un asiento de dos líneas;
una venta de productos genera un solo asiento de cuatro líneas que incluye ingreso y costo.
Cuando corresponda IVA se agregan sus líneas dentro del mismo asiento, según el apartado siguiente.
Así la venta tiene una única clave
contable; no se confirma el ingreso dejando pendiente otro envío para su costo. Una venta exclusivamente
de servicios lleva solo las líneas de ingreso, sin movimiento ni costo de mercancía.

Inventario entrega el costo mediante `InventoryMovementResult.TotalCost`; Ventas no lo reemplaza por el precio
de venta ni lo recalcula leyendo tablas ajenas. Esta dependencia aún requiere implementar/acordar INV-03 y ACUERDO-01.

Cada empresa debe tener identificados los códigos de Caja, Inventario, Ventas y Costo de ventas antes de confirmar,
y los de IVA compras recuperable e IVA ventas por pagar cuando la operación los requiera.
Los códigos de los ejemplos siguientes son ficticios y no constituyen un plan obligatorio para todas las empresas.
La interfaz/tabla de configuración completa de mapeos (ACC-06) queda fuera de este acuerdo;
la función de las cuentas de IVA sí se define aquí, sin imponer códigos universales.
Si falta una cuenta configurada, la operación se rechaza; no se crean cuentas ni se escogen otras automáticamente.

Ejemplos compilables de las solicitudes; definen datos, **no ejecutan el servicio todavía pendiente**:

```csharp
using ERP.Modules.Accounting.Contracts;

var purchaseId = Guid.Parse("0195a000-0000-7000-8000-000000000030");
var saleId = Guid.Parse("0195a000-0000-7000-8000-000000000020");
var date = new DateOnly(2026, 11, 20);

var purchase = new PostingRequest(
    IdempotencyKey: $"purchases:receipt:{purchaseId:D}",
    SourceModule: "purchases",
    SourceDocumentType: "receipt",
    SourceDocumentId: purchaseId,
    AccountingDate: date,
    Description: "Compra al contado: 10 cuadernos a USD 3",
    Lines:
    [
        new PostingLine("1.1.02", 30m, 0m, "Inventario"),
        new PostingLine("1.1.01", 0m, 30m, "Caja"),
    ]);

var sale = new PostingRequest(
    IdempotencyKey: $"sales:invoice:{saleId:D}",
    SourceModule: "sales",
    SourceDocumentType: "invoice",
    SourceDocumentId: saleId,
    AccountingDate: date,
    Description: "Venta al contado: 4 cuadernos a USD 5; costo total USD 12",
    Lines:
    [
        new PostingLine("1.1.01", 20m, 0m, "Caja"),
        new PostingLine("4.1.01", 0m, 20m, "Ventas"),
        new PostingLine("5.1.01", 12m, 0m, "Costo de ventas"),
        new PostingLine("1.1.02", 0m, 12m, "Inventario"),
    ]);
```

En la compra: debe = haber = 30. En la venta completa: debe = haber = 32 (ingreso 20 y costo 12).
El documento vendido conserva su importe de 20; 32 es la suma contable de las cuatro líneas.
Este ejemplo excluye impuestos, descuentos y crédito; no es configuración fiscal de producción.

### IVA de compras y ventas

Ampliación solicitada por Noe para ACUERDO-02. Define el tratamiento de una operación al contado con IVA;
no implementa todavía el cálculo comercial, un catálogo oficial, declaraciones, retenciones ni el servicio contable.

**Responsabilidades y datos fiscales.** Compras/Ventas calculan y validan el impuesto del documento antes de
solicitar el asiento. Usan el tratamiento del artículo (`VatTreatment`) y el código SRI aplicable, resuelto en
`TaxRateCatalog.Resolve(TaxKind.Iva, sriCode, taxDate)`. `taxDate` es la fecha del hecho generador tributario
que conserva el documento; no se sustituye automáticamente por `AccountingDate`, la hora del servidor o la fecha del reintento.
El enum no es un porcentaje ni un código SRI: su correspondencia debe estar configurada y verificada.

`Standard`, `ZeroRated`, `Exempt` y `NotSubject` son tratamientos distintos. Cero, exento y no objeto no se
convierten entre sí porque su importe de impuesto sea cero. Debe conservarse la clasificación del documento.
Para una tarifa aplicable se conserva un snapshot de código, porcentaje, base, impuesto, fecha tributaria,
versión/fuente del catálogo y tratamiento. La elegibilidad del IVA de compra como recuperable también se conserva.
Esos datos son responsabilidad del documento origen; `PostingRequest` sigue enviando códigos de cuenta e importes finales.
El enlace módulo/tipo/ID permite auditar desde el asiento al desglose fiscal del documento, sin leer tablas de otro módulo.

**Tarifas y verificación.** No fijar un porcentaje general en Contabilidad, Ventas, Compras ni en el producto.
El catálogo debe aportar vigencia y fuente oficial. Una tarifa ausente produce `tax_rate_not_found`;
un mapeo fiscal ausente o una tarifa no verificada bloquean la confirmación real, sin asumir cero.
`Resolve` comprueba existencia/vigencia, pero **no comprueba por sí mismo `VerificationStatus.Verificado`**;
esa comprobación corresponde al módulo origen. Los catálogos `Sintetico` sirven únicamente para pruebas aisladas.
`ITaxRateCatalogProvider` todavía no tiene implementación y no hay un catálogo oficial cargado en este repositorio;
esta definición no presenta esa dependencia como resuelta.

**Base y redondeo propuestos para el ejemplo.** Los precios de 3 y 5 son valores antes de IVA. Compras/Ventas
deben distinguir expresamente si sus precios incluyen el impuesto; no se aplica IVA otra vez sobre un total bruto.
Para el recorrido inicial se propone base por línea e IVA por línea a dos decimales con `decimal` y
`MidpointRounding.AwayFromZero`, sumando después los importes ya redondeados. D-05 y la compatibilidad de ese
cálculo con los documentos/comprobantes del equipo quedan sujetos a revisión; no se declara una regla universal del SRI.
Contabilidad recibe esos importes finales: no recalcula IVA ni inserta una diferencia para forzar el balance.
Si el desglose no coincide con los totales del documento, el módulo origen rechaza la confirmación.

**Cuentas por empresa.** Además de las cuatro cuentas base, identificar las siguientes cuando correspondan:

| Función | Tipo del plan y naturaleza del ejemplo | Uso |
|---|---|---|
| IVA compras recuperable | Activo, deudora | Debe por el IVA de adquisición que sí corresponda reconocer como recuperable |
| IVA ventas por pagar | Pasivo, acreedora | Haber por el IVA cobrado en la venta |

No se crea una cuenta automáticamente ni se reutiliza una cuenta de otra empresa. Los códigos ficticios del
ejemplo con IVA son `1.1.03` y `2.1.01`; cada empresa debe mapear sus códigos reales y permitir movimientos.
La configuración de mapeos se debe coordinar con el equipo aunque su pantalla/tabla completa corresponda a ACC-06.

**Compra con IVA recuperable.** Sea `IP` el IVA de la compra calculado por el módulo origen.
Si corresponde recuperar todo ese IVA, el asiento es:

| Cuenta | Debe | Haber |
|---|---|---|
| Inventario | 30 | 0 |
| IVA compras recuperable | IP | 0 |
| Caja | 0 | 30 + IP |

El IVA recuperable no se suma al costo del inventario. **No se presume que toda compra da derecho a crédito.**
Compras debe determinarlo según el documento y las reglas aplicables a la empresa. Si el IVA no es recuperable,
su destino como costo o gasto debe estar definido con Contabilidad e Inventario antes de confirmar; no se envía
a la cuenta de crédito tributario. El recorrido inicial no resuelve crédito parcial/proporcional ni permite confirmar
cuando ese tratamiento esté pendiente. La valoración del inventario debe ser coherente con cualquier IVA llevado al costo.

**Venta con IVA.** Sea `IV` el impuesto de la venta. Para productos, ingreso, IVA y costo se envían juntos:

| Cuenta | Debe | Haber |
|---|---|---|
| Caja | 20 + IV | 0 |
| Ventas | 0 | 20 |
| IVA ventas por pagar | 0 | IV |
| Costo de ventas | 12 | 0 |
| Inventario | 0 | 12 |

El impuesto cobrado se distingue del ingreso por ventas. El costo de salida es el valor ya determinado por
Inventario; no se calcula nuevamente IVA sobre ese asiento de costo. Una venta de servicios conserva las
líneas de Caja, Ventas e IVA cuando aplique, sin salida/costo de mercancía.
Cuando el impuesto sea cero se omite su línea contable, porque `PostingLine` no admite debe/haber ambos cero;
la clasificación fiscal cero/exento/no objeto permanece en el documento origen.

**Ejemplo principal de Ecuador con IVA general del 15%:** para la compra de base 30, IP = 4.50,
total pagado 34.50 y debe/haber 34.50. Para la venta de base 20, IV = 3.00, cobro total 23.00
y debe/haber del asiento completo 35.00 (23.00 + costo 12).
La referencia es la [Circular SRI NAC-DGECCGC25-00000006](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=236482f4-6125-42fd-b073-62c99d08233d&nombre=NAC-DGECCGC25-00000006.pdf),
que mantiene la tarifa general del 15% hasta que se modifique por decreto. No se extiende ese porcentaje
a artículos con tarifa cero, exentos, no objeto o a regímenes/fechas con una tarifa especial aplicable.
El siguiente helper obtiene el porcentaje del catálogo suministrado; no lo fija dentro de Compras/Ventas/Contabilidad,
no invoca el servicio ni valida la elegibilidad de un contribuyente. Las pruebas ejecutables usan un catálogo aislado,
que no sustituye el proveedor/catálogo oficial pendiente.
Su escenario presupone IVA de compra totalmente recuperable y precios antes de IVA:

```csharp
using ERP.Modules.Accounting.Contracts;
using ERP.Tributario.Tax;

static (PostingRequest Purchase, PostingRequest Sale) CreateVatExample(
    TaxRateCatalog catalog, string sriCode, DateOnly taxDate, Guid purchaseId, Guid saleId)
{
    var rate = catalog.Resolve(TaxKind.Iva, sriCode, taxDate);
    var purchaseVat = decimal.Round(30m * rate.Percentage / 100m, 2, MidpointRounding.AwayFromZero);
    var saleVat = decimal.Round(20m * rate.Percentage / 100m, 2, MidpointRounding.AwayFromZero);
    var purchaseLines = new List<PostingLine> { new("1.1.02", 30m, 0m, "Inventario") };
    if (purchaseVat > 0m)
    {
        purchaseLines.Add(new("1.1.03", purchaseVat, 0m, "IVA compras recuperable"));
    }

    purchaseLines.Add(new("1.1.01", 0m, 30m + purchaseVat, "Caja"));
    var saleLines = new List<PostingLine>
    {
        new("1.1.01", 20m + saleVat, 0m, "Caja"),
        new("4.1.01", 0m, 20m, "Ventas"),
    };
    if (saleVat > 0m)
    {
        saleLines.Add(new("2.1.01", 0m, saleVat, "IVA ventas por pagar"));
    }

    saleLines.Add(new("5.1.01", 12m, 0m, "Costo de ventas"));
    saleLines.Add(new("1.1.02", 0m, 12m, "Inventario"));
    var purchase = new PostingRequest($"purchases:receipt:{purchaseId:D}", "purchases", "receipt",
        purchaseId, taxDate, "Compra de prueba con IVA recuperable", purchaseLines);
    var sale = new PostingRequest($"sales:invoice:{saleId:D}", "sales", "invoice",
        saleId, taxDate, "Venta de prueba con IVA e inventario", saleLines);
    return (purchase, sale);
}
```

El helper iguala fecha tributaria y contable únicamente para este ejemplo. El módulo real debe conservar ambas
si difieren y comprobar que la tarifa sea oficial/verificada antes de confirmar o emitir; el helper acepta catálogos
sintéticos porque es un ejemplo aislado, no una ruta de producción. Para documentos con distintas tarifas,
se conserva el desglose fiscal por línea/grupo en el origen, sin aplicar una única tarifa a toda la factura.

El IVA forma parte del contenido protegido por la misma clave de idempotencia. Un reintento usa el snapshot
original y las mismas líneas; no vuelve a resolver otra tarifa con la fecha actual. Misma clave con IVA o total
distinto es conflicto, no otro asiento. Si falta tarifa, cuenta o tratamiento de recuperabilidad, la operación completa
debe fallar dentro del flujo de documento/stock/asiento ya descrito, sin éxito parcial.

**Fuentes y alcance:** [Circular SRI NAC-DGECCGC25-00000006](https://www.sri.gob.ec/o/sri-portlet-biblioteca-alfresco-internet/descargar?id=236482f4-6125-42fd-b073-62c99d08233d&nombre=NAC-DGECCGC25-00000006.pdf)
para la continuidad de la tarifa general del 15%, y [SRI: Impuesto al Valor Agregado](https://www.sri.gob.ec/impuesto-al-valor-agregado-iva)
para base, tratamientos y crédito sujeto a condiciones; consultadas para esta ampliación el 20/11/2026.
La tarifa del ejemplo se verifica con la circular específica, no solo con el texto general de la página informativa.
No se convierte en una constante del módulo ni sustituye la verificación de normativa por fecha/documento.
Retenciones, notas de crédito, declaraciones y crédito proporcional requieren sus propios acuerdos y no se habilitan aquí.
La separación del impuesto recuperable del costo de inventario del ejemplo también sigue la referencia de
[IAS 2, párrafo 11 (IFRS Foundation, edición 2021)](https://www.ifrs.org/content/dam/ifrs/publications/pdf-standards/english/2021/issued/part-a/ias-2-inventories.pdf),
que excluye del costo de compra los impuestos recuperables. Esta referencia contable no determina por sí sola
si una empresa tiene derecho al crédito tributario en Ecuador.

### Idempotencia y solicitudes simultáneas

- Clave canónica del ejemplo: `<modulo>:<tipo>:<uuid-en-formato-D>`, conservada en todos los reintentos.
  El emisor no genera otro UUID ni clave por intento y no reutiliza la clave para una corrección.
- La unicidad se aplica a `(tenant_id, idempotency_key)`, no globalmente. La misma clave en otra empresa
  es independiente; ninguna consulta de reintento debe saltarse el tenant.
- Propuesta para contenido repetido: comparar módulo, tipo, ID, fecha, descripción y todas las líneas,
  incluidos importes, Memo y orden, tras recortar textos y normalizar códigos de cuenta según ACC-01.
  El emisor debe conservar ese orden. Misma clave con datos distintos -> conflicto 409, sin sobrescribir.
- Buscar primero un asiento ya confirmado: si el reintento coincide, devolverlo aunque después se haya cerrado
  el período o desactivado una cuenta. Eso no autoriza un asiento nuevo con esas condiciones.
- La implementación de ACC-03 debe resolver dos peticiones simultáneas con restricción única en PostgreSQL.
  Un `AnyAsync` previo no garantiza idempotencia. Una colisión no puede dejar una transacción abortada
  y luego intentar continuar con consultas sin rollback/savepoint válido.

### Fallos y transacción del documento completo

| Situación | Resultado del contrato propuesto |
|---|---|
| Descripción vacía | `DomainException`, `journal_description_required`, 422 |
| Líneas insuficientes o importes inválidos | `journal_min_lines`, `journal_line_negative`, `journal_line_side` o `journal_line_scale`, 422 |
| Asiento desbalanceado | `DomainException`, `journal_unbalanced`, 422 |
| Cuenta no disponible para esa empresa | `DomainException`, `posting_account_not_available`, 422; nuevo código propuesto |
| Origen o clave no válidos | `DomainException`, `posting_source_invalid`, 422; nuevo código propuesto |
| Período cerrado | `DomainException`, `period_closed`, 422 |
| Misma clave con contenido distinto | `ConflictException`, `posting_idempotency_conflict`, 409; nuevo código propuesto |
| Cancelación, desconexión o fallo de base | No devolver un resultado de éxito; revertir la operación o consultar/reintentar con su misma identidad si el resultado quedó incierto |

Los códigos `journal_*` y `period_closed` ya existen en el dominio; los nuevos se implementarán en ACC-03.
Las excepciones internas no son respuestas HTTP: el host las transforma a `ProblemDetails` con `code` y `traceId`
usando el manejador común. Se mantienen 400/401/403 para empresa ausente, autenticación y autorización según
los contratos existentes. No se convierte cualquier error de infraestructura en un rechazo de negocio 422.

El caso de uso de Compras/Ventas controla el commit. Documento, movimiento de stock y asiento deben
guardarse en **una misma conexión y transacción PostgreSQL**, manteniendo la empresa durante toda la operación.
Los servicios participantes no hacen commits propios ni cambian el tenant. Ante una excepción se revierte todo;
la interfaz no confirma una venta o recepción parcialmente guardada.

**Brecha actual comprobada:** `AddModuleDbContext` registra contextos con conexiones independientes;
`InTenantTransactionAsync` solo reutiliza `CurrentTransaction` del contexto recibido. Llamar sucesivamente a ambos
servicios no implementa por sí solo la transacción compartida. Antes de habilitar la confirmación integrada,
el responsable de integración debe coordinar una conexión/transacción por caso de uso y enlistar los contextos,
por ejemplo mediante `UseTransactionAsync`. Los contratos siguen sin depender de EF, Npgsql ni ASP.NET.
Referencia: [transacciones entre contextos en EF Core](https://learn.microsoft.com/en-us/ef/core/saving/transactions#cross-context-transaction).

La política y el mecanismo para saber si un período está abierto todavía requieren acuerdo e implementación;
no se presume abierto por ausencia de catálogo. La llamada al SRI es externa y se recupera por estados,
no dentro de la transacción PostgreSQL. Si se separan bases en el futuro, se requiere otro acuerdo sobre outbox/consistencia.

### Reversos y aceptación

`ReverseAsync(...)` conserva su firma: debe generar otro asiento auditable y enlazado al original,
nunca borrarlo ni modificar sus líneas. Fecha, motivo, permiso y período deben validarse.
No se habilita esa operación hasta acordar la corrección del documento y sus efectos sobre inventario;
un reverso contable por sí solo no anula una venta ni devuelve stock.

Antes de aceptar #12, productor y consumidores revisan la agrupación de venta/costo/IVA, códigos por empresa,
tarifas y crédito recuperable, política monetaria inicial, transacción compartida, períodos y errores. Después de integrar el acuerdo y la primera
entrega completa, ACC-03 deberá probar: ejemplo 30/20/12, reintento idéntico y distinto, concurrencia,
aislamiento entre empresas, IVA positivo/cero, vigencias y falta de configuración fiscal, y rollback de documento/stock/asiento. Aquí se define ese comportamiento;
no se presenta como implementado ni probado todavía.

## Customers/Identification (Sales)

ACUERDO con Compras (Torres): el catálogo compartido de tipos de identificación usa estos valores:

| Valor API | Texto visible | Número máximo | Normalización |
|---|---|---|---|
| `Ruc` | RUC | 13 caracteres | Trim; solo dígitos; la estructura ecuatoriana se valida como advertencia |
| `Cedula` | Cédula | 10 caracteres | Trim; solo dígitos; el validador ecuatoriano se usa como advertencia |
| `Pasaporte` | Pasaporte | 20 caracteres | Trim; MAYÚSCULAS |
| `ConsumidorFinal` | Consumidor final | 20 caracteres | Trim; MAYÚSCULAS |

El cliente usa el mismo catálogo que los proveedores futuros. `EcuadorIdValidator` nunca rechaza
un documento por sí solo en esta entrega: su resultado se puede mostrar como advertencia y el
registro persiste. RUC, cédula, pasaporte y consumidor final son texto; no se usan identificadores
numéricos ni algoritmos locales como barreras de alta.

Reglas comunes:

- Nombre obligatorio, máximo 200 caracteres.
- Email opcional, máximo 320 caracteres y formato válido.
- Teléfono opcional, máximo 30 caracteres.
- Dirección opcional, máximo 300 caracteres.
- Baja lógica; no se elimina físicamente.
- Unicidad por `(tenant_id, identification_type, identification_number)`.
- El mismo número puede existir en otra empresa o con otro tipo.

## API CRUD de clientes

Ruta: `/api/v1/customers`.

| Operación | Ruta | Permiso |
|---|---|---|
| Listar | `GET /api/v1/customers` | `sales.customers.read` |
| Detalle | `GET /api/v1/customers/{id}` | `sales.customers.read` |
| Crear | `POST /api/v1/customers` | `sales.customers.write` |
| Editar | `PUT /api/v1/customers/{id}` | `sales.customers.write` |
| Activar/desactivar | `PATCH /api/v1/customers/{id}/status` | `sales.customers.write` |

La lista usa paginación común y búsqueda por nombre, número, email o teléfono.
Crear o editar devuelve `409` ante duplicado compuesto por empresa, tipo y número.
Los identificadores ajenos responden `404`; sin sesión `401`; sin permiso o empresa ajena `403`.

## Inventory.Contracts (Mendoza)
- `IProductCatalog.GetByIdsAsync`: **implementado** (INV-01). Solo artículos de la empresa activa; incluye `VatTreatment` (Ventas resuelve el código SRI y el porcentaje vigente con `TaxRateCatalog`), `TracksInventory` y `Barcode`; detalle en [modulos/inventario.md](modulos/inventario.md).
- `IStockQueryService.GetStockAsync` e `IInventoryMovementService.RegisterAsync`: reglas en ACUERDO-01 (#11), abajo.
  Hay una **implementación inicial** integrada en el PR #30 (sin stock negativo e idempotente); INV-03 (#15) la completa
  según el acuerdo. Las diferencias pendientes están en «Estado de la implementación».

## Inventory.Contracts — ACUERDO-01 (#11)

**Estado: propuesta de Mendoza para revisión de Compras (Torres) y Ventas (Casas).** Define cómo esos módulos
piden una entrada o salida de stock. Reemplaza la sección «ACUERDO-04» que se integró en el PR #30: era el mismo
tema con otro número; de ella se conservan la política sin stock negativo, la idempotencia, la consulta de existencias
y el fallo completo. Los importes de costo que devuelve Inventario son los que usan los asientos de ACUERDO-02 (#12).

### Quién pide qué

| Operación | Quién llama | `Kind` | `SourceModule` / `SourceDocumentType` | Costo unitario |
|---|---|---|---|---|
| Recepción de compra al contado | Compras (PUR-02) | `Entrada` | `purchases` / `receipt` | Obligatorio: el costo de la compra, sin IVA |
| Venta de productos | Ventas (SAL-02) | `Salida` | `sales` / `invoice` | `null`: lo calcula Inventario |
| Ajuste y transferencia | Nadie en este incremento | `Ajuste`, `Transferencia` | — | Rechazados con `inventory_movement_kind_not_supported` |

**Servicios y artículos sin control de inventario no generan movimiento.** El módulo origen consulta
`IProductCatalog.GetByIdsAsync` y solo envía las líneas con `TracksInventory = true`. Si ninguna línea controla
inventario (por ejemplo, una venta solo de servicios), no llama a `RegisterAsync`. Si envía un artículo sin control,
Inventario lo rechaza con `inventory_product_not_tracked`; no lo ignora en silencio.

### Datos de la solicitud

| Dato | Contrato |
|---|---|
| Empresa y usuario | `ITenantContext` de la solicitud autenticada; no hay `TenantId` en el DTO |
| `IdempotencyKey` | `<modulo>:<tipo>:<uuid-en-formato-D>` del documento, igual que en ACUERDO-02; la misma en cada reintento |
| `SourceDocumentId` | UUID del documento de la empresa activa; queda en el kardex para saber de dónde vino cada movimiento |
| `WarehouseId` | Bodega de la empresa activa. Cada empresa tiene una **bodega principal**: INV-03 crea las bodegas y agrega `IStockQueryService.GetDefaultWarehouseAsync` para obtenerla. El documento guarda la bodega usada |
| `MovementDate` | `DateOnly` de Ecuador del documento. Los movimientos se valoran **en el orden en que se registran**; no se recalcula la historia por fechas anteriores |
| `Lines` | Al menos una; cada artículo **una sola vez** (el módulo origen agrupa). Cantidad > 0 con escala máxima 4 |

### Existencias y costo

- **Sin stock negativo.** Una salida mayor que la existencia se rechaza completa con `inventory_insufficient_stock`
  (422); no se registra una parte. Decide D-06.
- **Costo promedio ponderado móvil** por artículo y bodega. Una entrada recalcula el promedio:
  `(existencia × promedio + cantidad × costo) / (existencia + cantidad)`. Una salida usa el promedio vigente y no lo cambia.
  El costo unitario se guarda con escala 4.
- `TotalCost` se devuelve en USD con **2 decimales** (redondeo `AwayFromZero`), que es la escala de los asientos
  (D-05). Compras lo usa para el debe de Inventario y Ventas para el par Costo de ventas / Inventario.
- **Concurrencia:** dos ventas simultáneas no pueden consumir la misma última unidad. INV-03 descuenta con una
  actualización condicional o un bloqueo de la fila de existencias dentro de la transacción; una consulta previa
  con `IStockQueryService` no garantiza el stock.
- `IStockQueryService.GetStockAsync(productId, warehouseId)` solo informa la disponibilidad para mostrarla en pantalla.
  Un saldo inexistente (`null`) se interpreta como 0 disponible; el disponible es `QuantityOnHand - QuantityReserved`,
  y `QuantityReserved` es 0 porque no hay reservas. INV-03 agrega la consulta de varios artículos para la pantalla de venta.

Ejemplo de la tarea, en una sola bodega: entrada de 10 cuadernos a USD 3 → existencia 10, promedio 3, `TotalCost` 30.
Salida de 4 → existencia 6, promedio 3, `TotalCost` 12.

```csharp
using ERP.Modules.Inventory.Contracts;

var receiptId = Guid.Parse("0195a000-0000-7000-8000-000000000030");
var invoiceId = Guid.Parse("0195a000-0000-7000-8000-000000000020");
var cuaderno = Guid.Parse("0195a000-0000-7000-8000-0000000000c1");
var bodega = Guid.Parse("0195a000-0000-7000-8000-0000000000b1"); // GetDefaultWarehouseAsync()
var date = new DateOnly(2026, 11, 20);

var entrada = new InventoryMovementRequest(
    IdempotencyKey: $"purchases:receipt:{receiptId:D}",
    SourceModule: "purchases",
    SourceDocumentType: "receipt",
    SourceDocumentId: receiptId,
    WarehouseId: bodega,
    Kind: InventoryMovementKind.Entrada,
    MovementDate: date,
    Lines: [new InventoryMovementLine(cuaderno, Quantity: 10m, UnitCost: 3m)]);   // TotalCost = 30

var salida = new InventoryMovementRequest(
    IdempotencyKey: $"sales:invoice:{invoiceId:D}",
    SourceModule: "sales",
    SourceDocumentType: "invoice",
    SourceDocumentId: invoiceId,
    WarehouseId: bodega,
    Kind: InventoryMovementKind.Salida,
    MovementDate: date,
    Lines: [new InventoryMovementLine(cuaderno, Quantity: 4m, UnitCost: null)]);  // TotalCost = 12
```

### Idempotencia

Mismas reglas que ACUERDO-02, para que Compras y Ventas manejen un solo criterio:

- Unicidad por `(tenant_id, idempotency_key)`. La misma clave en otra empresa es independiente.
- Reintento con el mismo contenido (módulo, tipo, documento, bodega, tipo de movimiento, fecha y líneas en el mismo orden):
  mismo `MovementId`, `AlreadyProcessed = true` y el **`TotalCost` original**, aunque el promedio haya cambiado después.
- Misma clave con contenido distinto: `ConflictException`, `inventory_idempotency_conflict`, 409, sin sobrescribir.
- Dos solicitudes simultáneas con la misma clave se resuelven con la restricción única de PostgreSQL, no con una consulta previa.

### Errores

| Situación | Código | HTTP |
|---|---|---|
| Sin líneas, artículo repetido, cantidad ≤ 0 o con más de 4 decimales | `inventory_lines_invalid`, `inventory_duplicate_product`, `inventory_quantity_invalid` | 422 |
| Entrada sin costo, costo negativo o con más de 4 decimales; Salida con costo | `inventory_unit_cost_required`, `inventory_unit_cost_invalid`, `inventory_unit_cost_not_allowed` | 422 |
| Artículo ajeno o inexistente, inactivo o sin control de inventario | `inventory_product_not_available`, `inventory_product_inactive`, `inventory_product_not_tracked` | 422 |
| Bodega ajena, inexistente o inactiva | `inventory_warehouse_not_available` | 422 |
| Existencia insuficiente para la salida | `inventory_insufficient_stock` | 422 |
| Ajuste o transferencia | `inventory_movement_kind_not_supported` | 422 |
| Módulo, tipo o clave no válidos | `inventory_source_invalid` | 422 |
| Misma clave con contenido distinto | `inventory_idempotency_conflict` | 409 |

Son `DomainException`/`ConflictException` del dominio; el host las convierte en `ProblemDetails` con `code`, como en el resto de la API.

### Fallos y transacción

Si el movimiento falla, **la compra o la venta no se confirma a medias**. El caso de uso de Compras o Ventas controla
el commit y, dentro de una sola transacción:

1. Guarda el documento.
2. Llama a `RegisterAsync` y obtiene `TotalCost` (solo si hay líneas con control de inventario).
3. Llama a `IAccountingPostingService.PostAsync` con los importes de ACUERDO-02.
4. Confirma. Ante cualquier excepción se revierte todo: documento, movimiento y asiento.

Las validaciones de catálogo, precio y stock fallan antes de confirmar el documento. Una respuesta incierta
(por ejemplo, se cortó la conexión) se reconcilia repitiendo con la **misma clave**; nunca se crea un segundo documento.

Inventario no hace commit propio ni cambia la empresa. Para que esto funcione se necesita la
**transacción compartida entre módulos** que ACUERDO-02 identificó como brecha: hoy cada `DbContext` abre su propia
conexión. Es un requisito común que se coordina como tarea aparte antes de habilitar la confirmación en PUR-02 y SAL-02.
Si en el futuro se separan las bases, se necesita otro acuerdo de compensación u *outbox*; no se acepta éxito parcial.

### Fuera de este acuerdo

Devoluciones, anulaciones, ajustes, transferencias, reservas, lotes y series. Los movimientos no se editan ni se
borran: una corrección futura será un movimiento nuevo enlazado al original, igual que los reversos contables.

### Decisiones para revisar

1. Sin stock negativo (D-06).
2. Costo promedio ponderado móvil por artículo y bodega; `TotalCost` con 2 decimales.
3. Una bodega principal por empresa; Compras y Ventas guardan la bodega en su documento.
4. El módulo origen agrupa las líneas por artículo y no envía servicios ni artículos sin control.
5. Misma clave e idempotencia que ACUERDO-02.
6. Orden documento → stock → asiento en una sola transacción.

### Estado de la implementación (PR #30)

El PR #30 integró una primera versión de `InventoryStockService` (movimientos y saldos) y su uso desde Ventas.
Ya cumple: sin stock negativo, idempotencia por clave dentro de la empresa y omisión de servicios en la salida.
INV-03 (#15) la ajusta a este acuerdo; hasta entonces **no se habilita la confirmación integrada** de compras y ventas.

| Tema | Hoy en `main` | Según ACUERDO-01 (INV-03) |
|---|---|---|
| Costo de la salida | Ventas envía el costo de la ficha (`ProductSummary.Cost`) y calcula ella el costo de ventas | Inventario calcula el costo promedio y lo devuelve en `TotalCost`; Ventas usa ese valor en el asiento |
| Bodegas | `WarehouseId` sin catálogo de bodegas | Tabla de bodegas con bodega principal por empresa y `GetDefaultWarehouseAsync` |
| Artículos sin control | Se omiten en silencio | El origen no los envía; si llegan, `inventory_product_not_tracked` |
| Reintento con otro contenido | Devuelve el movimiento original | 409 `inventory_idempotency_conflict` |
| Ajuste y transferencia | Aceptados | Rechazados hasta otro acuerdo |
| Códigos de error | `movement_*`, `stock_*` | `inventory_*` de la tabla de errores |
| Cuentas del asiento de venta | Nombres de cuenta («Costo de ventas», «Inventario») | Códigos del plan de la empresa (ACUERDO-02) |

## Tributario (`ERP.Tributario`)
- `EcuadorIdValidator` (sintáctico), `TaxRateCatalog` / `ITaxRateCatalogProvider` (tarifas por vigencia, nunca constantes), contratos SRI (`ISriGateway`, `IElectronicDocumentSigner`, `IAccessKeyGenerator`, `IElectronicDocumentXmlBuilder`).
  ACUERDO-03 fija simulación como nivel de esta entrega; el ambiente de pruebas y producción requieren inputs y verificaciones documentados en
  [`docs/validaciones/acuerdo-sri-nivel-entrega.md`](validaciones/acuerdo-sri-nivel-entrega.md).
- `EcuadorIdentificationCatalog` es la lista compartida por Clientes y Proveedores. Mantiene el código SRI, nombre y reglas de validación/uso para cada tipo; Ventas y Compras no deben declarar catálogos paralelos.

| Tipo | Código SRI | Regla del número | Clientes | Proveedores |
|---|---:|---|:---:|:---:|
| RUC | `04` | 13 dígitos; validación sintáctica con `EcuadorIdValidator` | Sí | Sí |
| Cédula | `05` | 10 dígitos; validación sintáctica con `EcuadorIdValidator` | Sí | Sí |
| Pasaporte | `06` | Texto de hasta 20 caracteres | Sí | Sí |
| Consumidor final | `07` | `9999999999999` | Sí | No |
| Identificación del exterior | `08` | Texto | Sí | Sí |

Normalización común: eliminar espacios y guiones antes de validar o guardar. El pasaporte además se convierte a mayúsculas. El módulo de Compras rechaza `07`, porque un proveedor siempre debe tener una identificación propia.

## Reporting ← módulos
- Cada módulo publica consultas de lectura (proyecciones) en su `*.Contracts` cuando Reporting las necesite. Prohibido el acceso directo a tablas ajenas.

## Convenciones CRUD comunes (v1)

Todo módulo nuevo sigue este patrón (implementado primero en Inventario):

- Rutas: `GET /api/v1/{recurso}` (lista), `GET /{id}`, `POST`, `PUT /{id}`, `PATCH /{id}/status` `{ isActive }`. Sin borrado físico: baja lógica.
- Paginación: `page` desde 1, `pageSize` 20 por defecto y máximo 100 (valores fuera de rango se ajustan), `search` opcional. Respuesta `PagedResult<T>` (`ERP.BuildingBlocks.Paging`): `items`, `totalCount`, `page`, `pageSize`, orden estable.
- Permisos: `modulo.recurso.read` y `modulo.recurso.write`.
- Errores (`ProblemDetails` con `code` y `traceId`): 400 validación con `errors` por campo · 401 sin sesión · 403 sin permiso o empresa ajena · 404 no existe en la empresa activa · **409 duplicado o conflicto** (`ConflictException`) · 422 otra regla de negocio (`DomainException`).
- Códigos de texto (SKU, código de unidad…): recortar espacios y MAYÚSCULAS; únicos por empresa.
- Escritorio: cada módulo crea su cliente (p. ej. `InventoryApi`) sobre `ErpApiClient.GetAsync<T>` / `SendJsonAsync<T>` sin editar el cliente común.

## API pública v1 (escritorio)
| Método | Ruta | Permiso |
|---|---|---|
| GET | `/api/v1/me` | autenticado |
| GET/POST/PUT/PATCH | `/api/v1/accounts` · `/{id}` · `/{id}/status` | `accounting.accounts.read` / `.write` |
| GET | `/api/v1/tenant/current` | `platform.tenant.read` |
| GET | `/api/v1/tenant/branches` · `/{id}` | `platform.branches.read` |
| POST | `/api/v1/tenant/branches` | `platform.branches.manage` |
| GET/POST/PUT/PATCH | `/api/v1/units` · `/{id}` · `/{id}/status` | `inventory.units.read` / `.write` |
| GET/POST/PUT/PATCH | `/api/v1/categories` · `/{id}` · `/{id}/status` | `inventory.categories.read` / `.write` |
| GET/POST/PUT/PATCH | `/api/v1/products` · `/{id}` · `/{id}/status` | `inventory.products.read` / `.write` |
| GET/POST/PUT/PATCH | `/api/v1/suppliers` · `/{id}` · `/{id}/status` | `purchases.suppliers.read` / `.write` |
| GET | `/api/v1/suppliers/identification-types` | `purchases.suppliers.read` |
| GET | `/api/v1/tenant/taxpayers` | `platform.taxpayers.read` |

Los modelos del escritorio (`ERP.Frontend.Shared/Api/ApiModels.cs`) se mantienen sincronizados manualmente con estos DTO.
