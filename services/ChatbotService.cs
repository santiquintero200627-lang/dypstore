using DYPStore.Data;
using DYPStore.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DYPStore.Services
{
    public enum ChatIntent
    {
        Greeting,
        SearchProducts,
        CheckPrice,
        CheckCategory,
        CheckPromotions,
        CheckAvailability,
        Recommendations,
        NewArrivals,
        BestSellers,
        BudgetSearch,
        Fitness,
        FAQ,
        // Admin only
        AdminInventory,
        AdminLowStock,
        AdminOutOfStock,
        AdminListUsers,
        AdminStats,
        AdminCreateProduct,
        AdminEditProduct,
        AdminDeleteProduct,
        AdminUpdateStock,
        AdminUpdatePrice,
        AdminFilterCategory,
        // Fallback
        Unknown
    }

    public class IntentResult
    {
        public ChatIntent Intent { get; set; }
        public string? ProductName { get; set; }
        public string? Category { get; set; }
        public decimal? Price { get; set; }
        public int? Stock { get; set; }
        public int? ProductId { get; set; }
        public int? StockThreshold { get; set; }
        public string? Color { get; set; }
        public string? Purpose { get; set; }
        public string? PriceRange { get; set; }
    }

    public class ChatbotResponse
    {
        public string Html { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }

    public class ChatbotService
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _um;
        private readonly ILogger<ChatbotService> _logger;
        private readonly IConfiguration _config;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        public ChatbotService(ApplicationDbContext db, UserManager<ApplicationUser> um, ILogger<ChatbotService> logger, IConfiguration config)
        {
            _db = db;
            _um = um;
            _logger = logger;
            _config = config;
        }

        // ═══════════════════════════════════════════════════════════════
        // INTENT DETECTION — Scoring-based NLP (tolerates typos, synonyms)
        // ═══════════════════════════════════════════════════════════════

        // Each entry: (intent, weight, keywords[])
        private static readonly (ChatIntent intent, int weight, string[] kws)[] _rules = new[]
        {
            // ── GREETING ──────────────────────────────────────────────────
            (ChatIntent.Greeting, 10, new[]{ "hola","buenos dias","buenas tardes","buenas noches","buenas","saludos","hey","hi","ola","que tal","como estas","buen dia","alo" }),

            // ── ADMIN WRITES (high weight to avoid misclassification) ──────
            (ChatIntent.AdminCreateProduct,20, new[]{ "crea un producto","crear producto","nuevo producto","agrega producto","agregar producto","anadir producto","añadir producto","add product","registra producto","registrar producto","quiero crear" }),
            (ChatIntent.AdminDeleteProduct,20, new[]{ "elimina el producto","eliminar producto","borra el producto","borrar producto","elimina producto","delete producto","quitar producto","remover producto","borra producto" }),
            (ChatIntent.AdminUpdatePrice, 18, new[]{ "edita el precio","actualiza el precio","cambia el precio","modifica el precio","precio a ","nuevo precio","cambia precio","sube el precio","baja el precio","modifica precio" }),
            (ChatIntent.AdminUpdateStock, 18, new[]{ "actualiza el stock","cambia el stock","modifica el stock","stock a ","nuevo stock","actualiza stock","edita stock","pon el stock","cambia las unidades","actualiza unidades" }),
            (ChatIntent.AdminEditProduct, 15, new[]{ "edita el producto","actualiza el producto","modifica el producto","editar producto","modificar producto","edita producto" }),

            // ── ADMIN READS ────────────────────────────────────────────────
            (ChatIntent.AdminLowStock,  15, new[]{ "bajo stock","pocas unidades","menos de ","stock menor","menor a ","pocos productos","escaso","pocas existencias","casi agotado","casi sin stock","menos unidades" }),
            (ChatIntent.AdminOutOfStock,15, new[]{ "agotado","sin stock","stock 0","stock cero","no hay stock","sin existencias","out of stock","productos agotados","cuales se agotaron","sin inventario","stock en 0" }),
            (ChatIntent.AdminInventory, 12, new[]{ "inventario","todo el stock","ver stock","mostrar stock","stock completo","existencias","ver inventario","mostrar inventario","cuanto hay","todo el inventario" }),
            (ChatIntent.AdminListUsers, 12, new[]{ "usuarios registrados","listar usuarios","mostrar usuarios","ver usuarios","cuantos usuarios","cuantos clientes","lista de usuarios","mis usuarios","cuantos registrados" }),
            (ChatIntent.AdminStats,     12, new[]{ "estadistica","estadisticas","resumen","total de productos","total de pedidos","ingresos","ventas totales","revenue","dashboard","cuanto hemos vendido","resumen general","informe" }),
            (ChatIntent.AdminFilterCategory, 8, new[]{ "filtrar categoria","categoria del admin","productos de la categoria","ver categoria" }),

            // ── NEW ARRIVALS ───────────────────────────────────────────────
            (ChatIntent.NewArrivals, 10, new[]{ "nuevo","nuevos","ultimos","recientes","recien llegado","que hay nuevo","que llego","novedades","recien agregado","productos nuevos","lo mas nuevo","ultimas novedades" }),

            // ── BEST SELLERS ───────────────────────────────────────────────
            (ChatIntent.BestSellers, 10, new[]{ "mas vendido","mejores productos","top productos","populares","los mas vendidos","cuales son los mejores","que recomiendas","mejor opcion","que me recomiendas","el mejor","cual es el mejor" }),

            // ── RECOMMENDATIONS ────────────────────────────────────────────
            (ChatIntent.Recommendations, 10, new[]{ "recomienda","recomendar","sugieres","sugiere","sugerencia","que me recomiendas","para que sirve","bueno para","sirve para","ideal para","para entrenar","para el gym","para gimnasio","para la competencia","para bajar de peso","para ganar musculo","para masa","para fuerza","para resistencia" }),

            // ── BUDGET ────────────────────────────────────────────────────
            (ChatIntent.BudgetSearch, 10, new[]{ "barato","economico","economica","precio bajo","mas economico","mas barato","algo economico","algo barato","bueno bonito barato","lo mas barato","precio menor","pocos pesos","no tan caro","asequible","lo que sea barato" }),

            // ── FITNESS ──────────────────────────────────────────────────
            (ChatIntent.Fitness, 8, new[]{ "gym","gimnasio","fitness","crossfit","entrenamiento","ejercicio","musculacion","musculatura","deporte","deportivo","atletismo","rendimiento","acondicionamiento" }),

            // ── CHECK PROMOTIONS ──────────────────────────────────────────
            (ChatIntent.CheckPromotions, 8, new[]{ "descuento","oferta","promocion","promo","rebaja","precio especial","sale","en oferta","estan ofertando","algo en descuento","con descuento","rebajado","tienen promociones" }),

            // ── CHECK AVAILABILITY ────────────────────────────────────────
            (ChatIntent.CheckAvailability, 8, new[]{ "disponible","hay stock","tienen stock","quedan","disponibilidad","en stock","hay disponibilidad","estan disponibles","cuantos quedan","lo tienen","lo hay","esta disponible" }),

            // ── CHECK PRICE ───────────────────────────────────────────────
            (ChatIntent.CheckPrice, 8, new[]{ "precio","cuanto cuesta","cuanto vale","cuesta","cuanto es","cuanto esta","cuanto tienen","vale","costo","coste","a cuanto","a que precio","a cuanto esta","tarifa" }),

            // ── CHECK CATEGORY ────────────────────────────────────────────
            (ChatIntent.CheckCategory, 6, new[]{ "categoria","categorias","tipos","que venden","que tienen","que productos","secciones","que tipo de productos","que clases de productos","que hay en la tienda" }),

            // ── SEARCH PRODUCTS ───────────────────────────────────────────
            (ChatIntent.SearchProducts, 5, new[]{ "busca","muestra","mostrar","ver","quiero","tienen","muestrame","buscar","search","encuentra","listar","ensenamen","enseñame","dame","dime","quiero ver","quiero comprar","donde esta","busco","quiero saber","tienes","existe","hay","tengan","cuales son" }),

            // ── FAQ ───────────────────────────────────────────────────────
            (ChatIntent.FAQ, 5, new[]{ "envio","entrega","devolucion","garantia","pago","contacto","como comprar","ayuda","soporte","cuanto tarda","despacho","cambio","devolver","metodo de pago","como se paga","atencion al cliente","politica","retorno" }),
        };

        public IntentResult DetectIntent(string message)
        {
            var raw = message.Trim();
            var m = RemoveDiacritics(raw.ToLower());
            var result = new IntentResult { Intent = ChatIntent.Unknown };

            // ── Extract numbers ────────────────────────────────────────
            var numbers = Regex.Matches(m, @"\d+(?:[.,]\d+)?");
            if (numbers.Count > 0)
            {
                var first = numbers[0].Value.Replace(",", ".");
                if (decimal.TryParse(first, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var num))
                {
                    result.Price = num;
                    result.Stock = (int)num;
                    result.StockThreshold = (int)num;
                    result.ProductId = (int)num;
                }
            }

            // ── Extract color ──────────────────────────────────────────
            var colors = new[] { "negro","negra","blanco","blanca","rojo","roja","azul","verde","gris","rosa","amarillo","naranja","morado","dorado" };
            foreach (var c in colors)
                if (m.Contains(c)) { result.Color = c; break; }

            // ── Extract purpose ────────────────────────────────────────
            var purposeMatch = Regex.Match(m, @"para\s+([\w\s]+?)(?:\s+y\s+|\s+o\s+|$|,|\.)");
            if (purposeMatch.Success)
                result.Purpose = purposeMatch.Groups[1].Value.Trim();

            // ── Extract price range ────────────────────────────────────
            if (m.Contains("menos de") || m.Contains("menor a") || m.Contains("maximo") || m.Contains("hasta"))
                result.PriceRange = "max";
            else if (m.Contains("mas de") || m.Contains("mayor a") || m.Contains("minimo") || m.Contains("desde"))
                result.PriceRange = "min";

            // ── Extract category ───────────────────────────────────────
            if (ContainsAny(m, "boxeo","guante","casco","venda","saco de boxeo","kick","muay","artes marciales","punch","sparring","mma"))
                result.Category = "boxing";
            else if (ContainsAny(m, "tenis","zapato","calzado","zapatilla","shoe","chancla","bota deportiva","running","correr","caminar","marcha","atletismo","pista"))
                result.Category = "shoes";
            else if (ContainsAny(m, "suplemento","proteina","creatina","vitamina","whey","aminoacido","bcaa","pre-workout","preworkout","colageno","omega","shaker","suero","gainer","masa muscular","quemador","fat burner"))
                result.Category = "supplements";

            // ── Extract product name ───────────────────────────────────
            var nameMatch = Regex.Match(m, @"(?:llamad[oa]|nombre|producto|el)\s+([a-z0-9\s]{2,40}?)(?:\s+a\s+\d|\s+precio|\s+stock|$)");
            if (nameMatch.Success)
                result.ProductName = nameMatch.Groups[1].Value.Trim();

            // ── Score-based matching ───────────────────────────────────
            var scores = new Dictionary<ChatIntent, int>();
            foreach (var (intent, weight, kws) in _rules)
            {
                foreach (var kw in kws)
                {
                    if (m.Contains(kw))
                    {
                        if (!scores.ContainsKey(intent)) scores[intent] = 0;
                        scores[intent] += weight + (kw.Length >= 6 ? 3 : 0); // longer match = more specific
                    }
                }
            }

            if (scores.Count > 0)
            {
                result.Intent = scores.OrderByDescending(kv => kv.Value).First().Key;
            }

            return result;
        }

        public async Task<ChatbotResponse> ProcessAsync(string message, bool isAdmin)
        {
            var intent = DetectIntent(message);
            _logger.LogDebug("CHATBOT intent={Intent} admin={Admin} msg={Msg}", intent.Intent, isAdmin, message);

            // ── Permission guard ──────────────────────────────────────
            var adminIntents = new[]
            {
                ChatIntent.AdminInventory, ChatIntent.AdminLowStock, ChatIntent.AdminOutOfStock,
                ChatIntent.AdminListUsers, ChatIntent.AdminStats, ChatIntent.AdminCreateProduct,
                ChatIntent.AdminEditProduct, ChatIntent.AdminDeleteProduct, ChatIntent.AdminUpdateStock,
                ChatIntent.AdminUpdatePrice, ChatIntent.AdminFilterCategory
            };

            if (adminIntents.Contains(intent.Intent) && !isAdmin)
            {
                _logger.LogWarning("USER attempted admin intent: {Intent} | msg: {Message}", intent.Intent, message);
                return Error("No tienes permisos para realizar esta acción.");
            }

            try
            {
                // 1. Ejecutar acciones administrativas directas (escrituras o grids específicos de admin)
                var adminActions = new[]
                {
                    ChatIntent.AdminInventory, ChatIntent.AdminLowStock, ChatIntent.AdminOutOfStock,
                    ChatIntent.AdminListUsers, ChatIntent.AdminStats, ChatIntent.AdminCreateProduct,
                    ChatIntent.AdminEditProduct, ChatIntent.AdminDeleteProduct, ChatIntent.AdminUpdateStock,
                    ChatIntent.AdminUpdatePrice, ChatIntent.AdminFilterCategory
                };

                if (adminActions.Contains(intent.Intent))
                {
                    return intent.Intent switch
                    {
                        ChatIntent.AdminInventory     => await AdminInventoryAsync(),
                        ChatIntent.AdminLowStock      => await AdminLowStockAsync(intent.StockThreshold ?? 5),
                        ChatIntent.AdminOutOfStock    => await AdminOutOfStockAsync(),
                        ChatIntent.AdminListUsers     => await AdminListUsersAsync(),
                        ChatIntent.AdminStats         => await AdminStatsAsync(),
                        ChatIntent.AdminCreateProduct => AdminCreateProductGuide(intent),
                        ChatIntent.AdminEditProduct   => await AdminEditProductGuideAsync(intent, message),
                        ChatIntent.AdminDeleteProduct => await AdminDeleteProductGuideAsync(intent, message),
                        ChatIntent.AdminUpdateStock   => await AdminUpdateStockAsync(intent, message),
                        ChatIntent.AdminUpdatePrice   => await AdminUpdatePriceAsync(intent, message),
                        ChatIntent.AdminFilterCategory=> await AdminFilterCategoryAsync(intent),
                        _                             => UnknownResponse(isAdmin)
                    };
                }

                // 2. Intentar llamar a IA si hay API Key configurada
                var geminiKey = _config["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
                var openAiKey = _config["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");

                if (!string.IsNullOrEmpty(geminiKey) || !string.IsNullOrEmpty(openAiKey))
                {
                    var products = await _db.Products.ToListAsync();
                    var catalogInfo = string.Join("\n", products.Select(p => $"- {p.Name} (ID: {p.Id}, Marca: {p.Brand}, Categoría: {CatLabel(p.Category)}, Precio: ${p.Price:N0}, Stock: {p.Stock} disp.): {p.Description}"));
                    var roleLabel = isAdmin ? "Administrador" : "Usuario regular / Público";

                    var systemPrompt = $@"Eres el asistente virtual inteligente oficial de la tienda deportiva DYPStore.
El rol actual del usuario es: {roleLabel}.

INSTRUCCIONES DE SEGURIDAD Y PERMISOS DE ROLES (¡CRÍTICO!):
- Si el usuario es 'Usuario regular / Público':
  * NO tienes acceso a datos del sistema de administración.
  * NO reveles correos electrónicos de clientes, registros de usuarios, estadísticas de ventas, ingresos, historial de chat de otros usuarios o detalles de inventario completo.
  * Si el usuario te pregunta por estadísticas, usuarios registrados o actualizar existencias/precios, debes denegar el acceso con amabilidad indicándole que solo los administradores autorizados pueden realizar estas operaciones.
  * NO realices acciones como eliminar productos, crear productos o cambiar precios/inventario.
- Si el usuario es 'Administrador':
  * Tienes permitido hablar sobre stock completo, existencias, lista de usuarios registrados e ingresos.
  * Si el administrador te solicita editar precios, stock, crear o eliminar un producto, guíalo indicándole cómo hacerlo usando los comandos rápidos del chat o en su respectivo panel del dashboard.

INFORMACIÓN DEL CATÁLOGO DE PRODUCTOS (Real en Base de Datos):
{catalogInfo}

POLÍTICAS Y PREGUNTAS FRECUENTES (FAQ):
- Envíos: Realizados en 24-48 horas hábiles a nivel nacional. El costo se calcula en base a la ubicación en el checkout.
- Devoluciones: Dentro de los 30 días posteriores a la entrega. El producto debe estar intacto, sin usar y en su empaque original.
- Garantía: Cobertura premium ante fallos de fábrica. Escribir a soporte@dypstore.com.
- Métodos de pago: Tarjeta de crédito, débito, PSE y pago contra entrega (para ciertas ciudades).
- Pasos para comprar: 1. Añadir al carrito. 2. Iniciar sesión. 3. Proceder al pago.

DIRECTRICES DE FORMATO DE RESPUESTA:
- Responde siempre en español.
- Sé servicial, entusiasta sobre el deporte y amigable.
- Tu respuesta DEBE ser en formato HTML sencillo y limpio para integrarse con la burbuja de chat de la web. Puedes usar etiquetas como <strong>, <em>, <br>, <ul>, <li>, o enlaces <a href='/Products/Details/ID'> para productos específicos. No uses bloques de código con markdown o etiquetas ```html. Escribe la respuesta directamente en HTML.
- Si recomiendas productos, incluye su nombre exacto y puedes enlazar a su detalle usando: <a href='/Products/Details/{{id}}' class='text-danger fw-bold'>{{nombre}}</a>.";

                    string? aiResponse = null;
                    if (!string.IsNullOrEmpty(geminiKey))
                    {
                        _logger.LogInformation("Calling Gemini AI API for message: {Msg}", message);
                        aiResponse = await CallGeminiAsync(systemPrompt, message, geminiKey);
                    }
                    else if (!string.IsNullOrEmpty(openAiKey))
                    {
                        _logger.LogInformation("Calling OpenAI API for message: {Msg}", message);
                        aiResponse = await CallOpenAIAsync(systemPrompt, message, openAiKey);
                    }

                    if (!string.IsNullOrEmpty(aiResponse))
                    {
                        return Ok(aiResponse);
                    }

                    _logger.LogWarning("AI Response was empty or failed. Falling back to local rules.");
                }

                // 3. Fallback a lógica de reglas locales
                return intent.Intent switch
                {
                    ChatIntent.Greeting           => Greeting(isAdmin),
                    ChatIntent.Recommendations    => await RecommendationsAsync(intent, message),
                    ChatIntent.NewArrivals        => await NewArrivalsAsync(),
                    ChatIntent.BestSellers        => await BestSellersAsync(),
                    ChatIntent.BudgetSearch       => await BudgetSearchAsync(intent),
                    ChatIntent.Fitness            => await FitnessAsync(intent),
                    ChatIntent.SearchProducts     => await SearchProductsAsync(intent, message),
                    ChatIntent.CheckPrice         => await CheckPriceAsync(intent, message),
                    ChatIntent.CheckCategory      => Categories(),
                    ChatIntent.CheckPromotions    => await PromotionsAsync(),
                    ChatIntent.CheckAvailability  => await CheckAvailabilityAsync(intent, message),
                    ChatIntent.FAQ                => FAQ(message),
                    _                             => UnknownResponse(isAdmin)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chatbot error processing: {Message}", message);
                return Error("Ocurrió un error al procesar tu consulta. Intenta de nuevo.");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // USER HANDLERS
        // ═══════════════════════════════════════════════════════════════

        private ChatbotResponse Greeting(bool isAdmin)
        {
            var role = isAdmin ? "Administrador" : "usuario";
            var hint = isAdmin
                ? "<div class='dyp-chat-hint'>Como admin puedes: consultar inventario, usuarios, estadísticas, crear/editar/eliminar productos y más.</div>"
                : "<div class='dyp-chat-hint'>Puedo ayudarte con: buscar productos, precios, disponibilidad, categorías, recomendaciones y más. ¡Pregúntame lo que quieras!</div>";

            return Ok($@"<div class='dyp-chat-greeting'>
                <div class='dyp-chat-icon'>⚡</div>
                <div>
                    <strong>¡Hola, {role}! Soy el asistente de DYPStore.</strong><br/>
                    <span class='dyp-chat-sub'>Estoy aquí para ayudarte con todo lo que necesites.</span>
                </div>
            </div>{hint}");
        }

        private async Task<ChatbotResponse> RecommendationsAsync(IntentResult intent, string message)
        {
            var m = RemoveDiacritics(message.ToLower());
            var q = _db.Products.Where(p => p.Stock > 0).AsQueryable();

            string title = "⭐ Recomendaciones";
            string subtitle = "Estos productos te pueden interesar";

            // Purpose-based filtering
            if (ContainsAny(m, "boxeo","guante","saco","venda","casco","kick","mma","artes marciales"))
            {
                q = q.Where(p => p.Category == ProductCategory.boxing);
                title = "🥊 Recomendaciones de Boxeo";
                subtitle = "Equipo de alto rendimiento para boxeo";
            }
            else if (ContainsAny(m, "correr","running","atletismo","trotar","pista","marcha"))
            {
                q = q.Where(p => p.Category == ProductCategory.shoes);
                title = "👟 Tenis para Correr";
                subtitle = "Calzado ideal para tu entrenamiento";
            }
            else if (ContainsAny(m, "proteina","masa","musculo","whey","ganar peso","ganar musculo","volumen"))
            {
                q = q.Where(p => p.Category == ProductCategory.supplements);
                title = "💊 Suplementos para Masa Muscular";
                subtitle = "Los mejores para ganar músculo";
            }
            else if (ContainsAny(m, "bajar de peso","perder peso","quemar grasa","adelgazar","definir","fat burner","quemador"))
            {
                q = q.Where(p => p.Category == ProductCategory.supplements);
                title = "🔥 Para Perder Peso / Definir";
                subtitle = "Suplementos y productos enfocados en definición";
            }
            else if (ContainsAny(m, "gym","gimnasio","fitness","crossfit","entrenamiento","fuerza","resistencia"))
            {
                title = "💪 Para el Gym";
                subtitle = "Lo mejor para tu entrenamiento";
            }
            else if (intent.Category != null && Enum.TryParse<ProductCategory>(intent.Category, out var cat))
            {
                q = q.Where(p => p.Category == cat);
            }

            var products = await q.OrderByDescending(p => p.Stock).Take(6).ToListAsync();
            if (!products.Any())
                products = await _db.Products.Where(p => p.Stock > 0).OrderByDescending(p => p.CreatedAt).Take(6).ToListAsync();

            return BuildProductGrid(products, title, subtitle);
        }

        private async Task<ChatbotResponse> NewArrivalsAsync()
        {
            var products = await _db.Products
                .OrderByDescending(p => p.CreatedAt)
                .Take(6)
                .ToListAsync();

            return BuildProductGrid(products, "🆕 Últimas novedades", "Los productos más recientes en DYPStore");
        }

        private async Task<ChatbotResponse> BestSellersAsync()
        {
            // Use high stock as proxy for popular products (in a real system, use order count)
            var products = await _db.Products
                .Where(p => p.Stock > 0)
                .OrderByDescending(p => p.Stock)
                .Take(6)
                .ToListAsync();

            return BuildProductGrid(products, "🏆 Los más populares", "Productos favoritos de nuestra comunidad");
        }

        private async Task<ChatbotResponse> BudgetSearchAsync(IntentResult intent)
        {
            var q = _db.Products.Where(p => p.Stock > 0).AsQueryable();

            if (intent.Category != null && Enum.TryParse<ProductCategory>(intent.Category, out var cat))
                q = q.Where(p => p.Category == cat);

            // Apply max price from extracted number
            if (intent.Price.HasValue && intent.Price > 0)
                q = q.Where(p => p.Price <= intent.Price.Value);

            var products = await q.OrderBy(p => p.Price).Take(6).ToListAsync();

            if (!products.Any())
                return Ok("<div class='dyp-chat-empty'><i class='bi bi-search'></i> No encontré productos económicos en ese rango. Prueba con otro presupuesto.</div>");

            return BuildProductGrid(products, "💸 Mejores opciones económicas", "Ordenados de menor a mayor precio");
        }

        private async Task<ChatbotResponse> FitnessAsync(IntentResult intent)
        {
            var products = await _db.Products
                .Where(p => p.Stock > 0)
                .OrderByDescending(p => p.CreatedAt)
                .Take(8)
                .ToListAsync();

            return BuildProductGrid(products, "💪 Equipamiento Fitness", "Todo lo que necesitas para tu entrenamiento");
        }

        private async Task<ChatbotResponse> SearchProductsAsync(IntentResult intent, string message)
        {
            var m = RemoveDiacritics(message.ToLower());
            var q = _db.Products.AsQueryable();

            if (intent.Category != null && Enum.TryParse<ProductCategory>(intent.Category, out var cat))
            {
                q = q.Where(p => p.Category == cat);
            }
            else if (!string.IsNullOrEmpty(intent.ProductName))
            {
                q = q.Where(p => p.Name.ToLower().Contains(intent.ProductName!.ToLower())
                               || p.Description.ToLower().Contains(intent.ProductName!.ToLower()));
            }
            else
            {
                // Try extracting keywords from the full message (filter stopwords)
                var stopwords = new HashSet<string>{ "que","como","cual","cuales","los","las","un","una","de","del","el","la","en","y","o","a","si","no","me","te","se","su","sus","mis","mas","muy","pero","por","para","con","sin","sobre","entre","cada","todo","toda","todos","todas","este","esta","estos","estas","ese","esa","esos","esas","hay","tiene","tienen","puedo","quiero","necesito","busco","dame" };
                var words = m.Split(new[]{ ' ','?','¿','!','¡',',','.' }, StringSplitOptions.RemoveEmptyEntries)
                             .Where(w => w.Length > 3 && !stopwords.Contains(w))
                             .Distinct()
                             .ToArray();

                if (words.Any())
                    q = q.Where(p => words.Any(w => p.Name.ToLower().Contains(w) || p.Description.ToLower().Contains(w)));
            }

            var products = await q.OrderByDescending(p => p.CreatedAt).Take(6).ToListAsync();

            if (!products.Any())
                return Ok("<div class='dyp-chat-empty'><i class='bi bi-search'></i> No encontré productos con esa búsqueda. Prueba con otros términos o pregúntame por categorías disponibles.</div>");

            var categoryLabel = intent.Category switch
            {
                "boxing"      => "Boxeo",
                "shoes"       => "Tenis Deportivos",
                "supplements" => "Suplementos",
                _             => "Productos"
            };

            return BuildProductGrid(products, $"🛍️ {categoryLabel} encontrados", $"{products.Count} resultados");
        }

        private async Task<ChatbotResponse> CheckPriceAsync(IntentResult intent, string message)
        {
            var m = RemoveDiacritics(message.ToLower());
            var stopwords = new HashSet<string>{ "cual","cuanto","cuesta","vale","precio","tiene","tienen","producto","como","cuestan" };
            var words = m.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                         .Where(w => w.Length > 3 && !stopwords.Contains(w))
                         .ToArray();

            if (!words.Any() && intent.Category == null)
                return Ok("<div class='dyp-chat-empty'>¿De qué producto quieres saber el precio? Ejemplo: <em>\"¿Cuánto cuestan los guantes de boxeo?\"</em></div>");

            var q = _db.Products.AsQueryable();
            if (intent.Category != null && Enum.TryParse<ProductCategory>(intent.Category, out var cat))
                q = q.Where(p => p.Category == cat);
            else if (words.Any())
                q = q.Where(p => words.Any(w => p.Name.ToLower().Contains(w) || p.Description.ToLower().Contains(w)));

            var products = await q.OrderBy(p => p.Price).Take(6).ToListAsync();
            if (!products.Any())
                return Ok("<div class='dyp-chat-empty'>No encontré productos que coincidan. Intenta con otro nombre.</div>");

            var sb = new StringBuilder();
            sb.Append("<div class='dyp-chat-section-title'>💰 Precios</div><div class='dyp-chat-price-list'>");
            foreach (var p in products)
                sb.Append($"<div class='dyp-chat-price-row'><span>{p.Name}</span><strong>${p.Price:N0}</strong></div>");
            sb.Append("</div>");
            return Ok(sb.ToString());
        }

        private ChatbotResponse Categories()
        {
            return Ok(@"<div class='dyp-chat-section-title'>📂 Categorías disponibles</div>
            <div class='dyp-chat-categories'>
                <a href='/Products?category=boxing' class='dyp-chat-cat-card boxing'>
                    <span class='icon'>🥊</span><span>Boxeo</span><small>Guantes, cascos, vendas y más</small>
                </a>
                <a href='/Products?category=shoes' class='dyp-chat-cat-card shoes'>
                    <span class='icon'>👟</span><span>Tenis Deportivos</span><small>Calzado de alto rendimiento</small>
                </a>
                <a href='/Products?category=supplements' class='dyp-chat-cat-card supplements'>
                    <span class='icon'>💊</span><span>Suplementos</span><small>Proteínas, creatina y más</small>
                </a>
            </div>");
        }

        private async Task<ChatbotResponse> PromotionsAsync()
        {
            var products = await _db.Products
                .Where(p => p.Stock > 0)
                .OrderBy(p => p.Price)
                .Take(4)
                .ToListAsync();

            if (!products.Any())
                return Ok("<div class='dyp-chat-empty'>Actualmente no hay promociones especiales. ¡Visita nuestra tienda para ver todos los productos!</div>");

            return BuildProductGrid(products, "🔥 Mejores precios actuales", "Los productos más accesibles");
        }

        private async Task<ChatbotResponse> CheckAvailabilityAsync(IntentResult intent, string message)
        {
            var m = RemoveDiacritics(message.ToLower());
            var q = _db.Products.AsQueryable();
            if (intent.Category != null && Enum.TryParse<ProductCategory>(intent.Category, out var cat))
                q = q.Where(p => p.Category == cat);

            var products = await q.OrderByDescending(p => p.Stock).Take(5).ToListAsync();
            if (!products.Any())
                return Ok("<div class='dyp-chat-empty'>No encontré productos para consultar disponibilidad.</div>");

            var sb = new StringBuilder();
            sb.Append("<div class='dyp-chat-section-title'>📦 Disponibilidad</div><div class='dyp-chat-price-list'>");
            foreach (var p in products)
                sb.Append($"<div class='dyp-chat-price-row'><span>{p.Name}</span><strong class='{(p.Stock > 0 ? "text-success" : "text-danger")}'>{(p.Stock > 0 ? $"✅ {p.Stock} en stock" : "❌ Agotado")}</strong></div>");
            sb.Append("</div>");
            return Ok(sb.ToString());
        }

        private ChatbotResponse FAQ(string message)
        {
            var m = RemoveDiacritics(message.ToLower());
            if (ContainsAny(m, "envio", "entrega", "cuanto tarda", "despacho", "cuando llega", "tiempo de entrega"))
                return Ok("<div class='dyp-chat-faq'><strong>🚚 Envíos</strong><br/>Realizamos envíos en 24-48 horas hábiles a todo el país. El costo depende de tu ubicación y se calcula al momento del pago.</div>");
            if (ContainsAny(m, "devolucion", "cambio", "devolver", "retorno", "regresa"))
                return Ok("<div class='dyp-chat-faq'><strong>🔄 Devoluciones</strong><br/>Aceptamos devoluciones dentro de los 30 días posteriores a la compra. El producto debe estar en su empaque original sin usar.</div>");
            if (ContainsAny(m, "garantia"))
                return Ok("<div class='dyp-chat-faq'><strong>🛡️ Garantía</strong><br/>Todos nuestros productos tienen garantía premium. Contacta nuestro soporte en soporte@dypstore.com para asistencia.</div>");
            if (ContainsAny(m, "pago", "como pago", "medios de pago", "formas de pago", "como se paga"))
                return Ok("<div class='dyp-chat-faq'><strong>💳 Métodos de pago</strong><br/>Aceptamos tarjetas de crédito/débito, PSE y pago contra entrega en algunas ciudades.</div>");
            if (ContainsAny(m, "contacto", "soporte", "ayuda", "atencion al cliente"))
                return Ok("<div class='dyp-chat-faq'><strong>📞 Contacto</strong><br/>Escríbenos a <strong>soporte@dypstore.com</strong> o síguenos en nuestras redes sociales.</div>");
            if (ContainsAny(m, "como comprar", "como realizo", "pasos para comprar"))
                return Ok("<div class='dyp-chat-faq'><strong>🛒 ¿Cómo comprar?</strong><br/>1. Busca el producto que quieres. 2. Agrégalo al carrito. 3. Inicia sesión (o crea una cuenta). 4. Confirma tu pedido. ¡Listo!</div>");
            return Ok("<div class='dyp-chat-faq'>¿En qué te puedo ayudar? Puedo responder preguntas sobre envíos, devoluciones, garantía y métodos de pago.</div>");
        }

        private ChatbotResponse UnknownResponse(bool isAdmin)
        {
            var hint = isAdmin
                ? "Ejemplos: <em>\"Muéstrame productos agotados\"</em>, <em>\"Crea un producto\"</em>, <em>\"Ver usuarios registrados\"</em>"
                : "Ejemplos: <em>\"Quiero guantes de boxeo\"</em>, <em>\"¿Qué proteína me recomiendas para ganar músculo?\"</em>, <em>\"¿Cuánto cuestan los tenis?\"</em>, <em>\"¿Qué hay nuevo?\"</em>";

            return Ok($@"<div class='dyp-chat-empty'>
                <i class='bi bi-question-circle' style='font-size:1.4rem;'></i>
                <div>No entendí bien tu pregunta. Inténtalo de otra forma.<br/><small class='text-secondary'>{hint}</small></div>
            </div>");
        }

        // ═══════════════════════════════════════════════════════════════
        // ADMIN HANDLERS
        // ═══════════════════════════════════════════════════════════════

        private async Task<ChatbotResponse> AdminInventoryAsync()
        {
            var products = await _db.Products.OrderBy(p => p.Name).Take(20).ToListAsync();
            if (!products.Any()) return Ok("<div class='dyp-chat-empty'>No hay productos en el inventario.</div>");

            var sb = new StringBuilder();
            sb.Append($"<div class='dyp-chat-section-title'>📦 Inventario completo ({products.Count} productos)</div>");
            sb.Append("<div class='dyp-chat-table-wrap'><table class='dyp-chat-table'><thead><tr><th>Producto</th><th>Stock</th><th>Precio</th><th>Categoría</th></tr></thead><tbody>");
            foreach (var p in products)
            {
                var stockClass = p.Stock == 0 ? "text-danger" : p.Stock <= 5 ? "text-warning" : "text-success";
                sb.Append($"<tr><td>{p.Name}</td><td class='{stockClass}'>{p.Stock}</td><td>${p.Price:N0}</td><td>{CatLabel(p.Category)}</td></tr>");
            }
            sb.Append("</tbody></table></div>");
            sb.Append("<a href='/Admin/Dashboard/Products' class='dyp-chat-link'>Ver panel completo →</a>");
            return Ok(sb.ToString());
        }

        private async Task<ChatbotResponse> AdminLowStockAsync(int threshold)
        {
            var products = await _db.Products
                .Where(p => p.Stock > 0 && p.Stock <= threshold)
                .OrderBy(p => p.Stock).ToListAsync();

            if (!products.Any())
                return Ok($"<div class='dyp-chat-empty'>✅ No hay productos con stock menor a {threshold} unidades.</div>");

            var sb = new StringBuilder();
            sb.Append($"<div class='dyp-chat-section-title'>⚠️ Bajo stock (≤{threshold} unidades) — {products.Count} producto(s)</div>");
            sb.Append("<div class='dyp-chat-table-wrap'><table class='dyp-chat-table'><thead><tr><th>Producto</th><th>Stock</th><th>Precio</th></tr></thead><tbody>");
            foreach (var p in products)
                sb.Append($"<tr><td>{p.Name}</td><td class='text-warning fw-bold'>{p.Stock}</td><td>${p.Price:N0}</td></tr>");
            sb.Append("</tbody></table></div>");
            return Ok(sb.ToString());
        }

        private async Task<ChatbotResponse> AdminOutOfStockAsync()
        {
            var products = await _db.Products.Where(p => p.Stock == 0).OrderBy(p => p.Name).ToListAsync();
            if (!products.Any()) return Ok("<div class='dyp-chat-empty'>✅ No hay productos agotados. ¡Excelente!</div>");

            var sb = new StringBuilder();
            sb.Append($"<div class='dyp-chat-section-title'>❌ Productos agotados — {products.Count}</div>");
            sb.Append("<div class='dyp-chat-table-wrap'><table class='dyp-chat-table'><thead><tr><th>Producto</th><th>Precio</th><th>Categoría</th></tr></thead><tbody>");
            foreach (var p in products)
                sb.Append($"<tr><td>{p.Name}</td><td>${p.Price:N0}</td><td>{CatLabel(p.Category)}</td></tr>");
            sb.Append("</tbody></table></div>");
            return Ok(sb.ToString());
        }

        private async Task<ChatbotResponse> AdminListUsersAsync()
        {
            var users = await _um.Users.OrderByDescending(u => u.CreatedAt).Take(15).ToListAsync();
            var sb = new StringBuilder();
            sb.Append($"<div class='dyp-chat-section-title'>👥 Usuarios registrados — {_um.Users.Count()} total</div>");
            sb.Append("<div class='dyp-chat-table-wrap'><table class='dyp-chat-table'><thead><tr><th>Nombre</th><th>Email</th><th>Registro</th></tr></thead><tbody>");
            foreach (var u in users)
                sb.Append($"<tr><td>{u.FullName}</td><td class='text-secondary'>{u.Email}</td><td>{u.CreatedAt.ToLocalTime():dd/MM/yy}</td></tr>");
            sb.Append("</tbody></table></div>");
            sb.Append("<a href='/Admin/Dashboard/Users' class='dyp-chat-link'>Ver todos →</a>");
            return Ok(sb.ToString());
        }

        private async Task<ChatbotResponse> AdminStatsAsync()
        {
            var totalProducts = await _db.Products.CountAsync();
            var totalOrders = await _db.Orders.CountAsync();
            var totalRevenue = await _db.Orders.Where(o => o.Status == OrderStatus.completed).SumAsync(o => (decimal?)o.Total) ?? 0;
            var totalUsers = _um.Users.Count();
            var lowStock = await _db.Products.CountAsync(p => p.Stock > 0 && p.Stock <= 5);
            var outOfStock = await _db.Products.CountAsync(p => p.Stock == 0);

            return Ok($@"<div class='dyp-chat-section-title'>📊 Resumen del negocio</div>
            <div class='dyp-chat-stats-grid'>
                <div class='dyp-chat-stat'><span class='val'>{totalProducts}</span><span class='lbl'>Productos</span></div>
                <div class='dyp-chat-stat'><span class='val'>{totalOrders}</span><span class='lbl'>Pedidos</span></div>
                <div class='dyp-chat-stat'><span class='val'>{totalUsers}</span><span class='lbl'>Usuarios</span></div>
                <div class='dyp-chat-stat'><span class='val'>${totalRevenue:N0}</span><span class='lbl'>Ingresos</span></div>
                <div class='dyp-chat-stat text-warning'><span class='val'>{lowStock}</span><span class='lbl'>Bajo stock</span></div>
                <div class='dyp-chat-stat text-danger'><span class='val'>{outOfStock}</span><span class='lbl'>Agotados</span></div>
            </div>
            <a href='/Admin/Dashboard' class='dyp-chat-link'>Ver dashboard completo →</a>");
        }

        private ChatbotResponse AdminCreateProductGuide(IntentResult intent)
        {
            var name = intent.ProductName ?? "Nuevo Producto";
            return Ok($@"<div class='dyp-chat-section-title'>➕ Crear producto</div>
            <div class='dyp-chat-faq'>Para crear un producto ve al panel admin o usa el enlace de abajo. Si me dices el nombre, precio, stock y categoría te redireccionaré con los datos prellenados.</div>
            <a href='/Admin/Dashboard/CreateProduct' class='dyp-chat-link'>Ir a crear producto →</a>");
        }

        private async Task<ChatbotResponse> AdminEditProductGuideAsync(IntentResult intent, string message)
        {
            if (intent.ProductId.HasValue)
            {
                var p = await _db.Products.FindAsync(intent.ProductId.Value);
                if (p != null)
                    return Ok($"<div class='dyp-chat-faq'>Producto encontrado: <strong>{p.Name}</strong><br/>Precio: ${p.Price:N0} | Stock: {p.Stock}</div><a href='/Admin/Dashboard/EditProduct/{p.Id}' class='dyp-chat-link'>Editar este producto →</a>");
            }

            if (!string.IsNullOrEmpty(intent.ProductName))
            {
                var match = await _db.Products.Where(p => p.Name.ToLower().Contains(intent.ProductName!.ToLower())).FirstOrDefaultAsync();
                if (match != null)
                    return Ok($"<div class='dyp-chat-faq'>Encontré: <strong>{match.Name}</strong></div><a href='/Admin/Dashboard/EditProduct/{match.Id}' class='dyp-chat-link'>Editar este producto →</a>");
            }

            return Ok("<div class='dyp-chat-faq'>Para editar un producto dime su ID o nombre. Ejemplo: <em>\"Edita el producto 5\"</em></div><a href='/Admin/Dashboard/Products' class='dyp-chat-link'>Ver todos los productos →</a>");
        }

        private async Task<ChatbotResponse> AdminDeleteProductGuideAsync(IntentResult intent, string message)
        {
            if (!string.IsNullOrEmpty(intent.ProductName))
            {
                var match = await _db.Products.Where(p => p.Name.ToLower().Contains(intent.ProductName!.ToLower())).FirstOrDefaultAsync();
                if (match != null)
                    return Ok($@"<div class='dyp-chat-faq'>⚠️ ¿Confirmas eliminar <strong>{match.Name}</strong>?<br/><small>Esta acción no se puede deshacer desde el chat. Hazlo desde el panel.</small></div>
                    <a href='/Admin/Dashboard/Products' class='dyp-chat-link'>Ir a gestionar productos →</a>");
            }

            return Ok("<div class='dyp-chat-faq'>Para eliminar un producto dime su nombre. Por seguridad la eliminación se hace desde el panel admin.</div><a href='/Admin/Dashboard/Products' class='dyp-chat-link'>Ir a gestionar productos →</a>");
        }

        private async Task<ChatbotResponse> AdminUpdateStockAsync(IntentResult intent, string message)
        {
            if (!string.IsNullOrEmpty(intent.ProductName) && intent.Stock.HasValue)
            {
                var p = await _db.Products.Where(x => x.Name.ToLower().Contains(intent.ProductName!.ToLower())).FirstOrDefaultAsync();
                if (p != null)
                {
                    var old = p.Stock;
                    p.Stock = intent.Stock.Value;
                    await _db.SaveChangesAsync();
                    return Ok($"<div class='dyp-chat-faq'>✅ Stock actualizado.<br/><strong>{p.Name}</strong><br/>Antes: {old} → Ahora: <strong>{p.Stock}</strong> unidades</div>");
                }
                return Ok($"<div class='dyp-chat-empty'>No encontré el producto <em>\"{intent.ProductName}\"</em>. Verifica el nombre.</div>");
            }
            return Ok("<div class='dyp-chat-faq'>Para actualizar el stock dime: <em>\"Actualiza el stock de [producto] a [cantidad]\"</em></div>");
        }

        private async Task<ChatbotResponse> AdminUpdatePriceAsync(IntentResult intent, string message)
        {
            if (!string.IsNullOrEmpty(intent.ProductName) && intent.Price.HasValue && intent.Price > 0)
            {
                var p = await _db.Products.Where(x => x.Name.ToLower().Contains(intent.ProductName!.ToLower())).FirstOrDefaultAsync();
                if (p != null)
                {
                    var old = p.Price;
                    p.Price = intent.Price.Value;
                    await _db.SaveChangesAsync();
                    return Ok($"<div class='dyp-chat-faq'>✅ Precio actualizado.<br/><strong>{p.Name}</strong><br/>Antes: ${old:N0} → Ahora: <strong>${p.Price:N0}</strong></div>");
                }
                return Ok($"<div class='dyp-chat-empty'>No encontré el producto <em>\"{intent.ProductName}\"</em>. Verifica el nombre.</div>");
            }
            return Ok("<div class='dyp-chat-faq'>Para actualizar el precio dime: <em>\"Cambia el precio de [producto] a [valor]\"</em></div>");
        }

        private async Task<ChatbotResponse> AdminFilterCategoryAsync(IntentResult intent)
        {
            if (intent.Category == null)
                return Ok("<div class='dyp-chat-empty'>¿Qué categoría quieres ver? (boxeo, tenis, suplementos)</div>");

            if (!Enum.TryParse<ProductCategory>(intent.Category, out var cat))
                return Ok("<div class='dyp-chat-empty'>Categoría no reconocida.</div>");

            var products = await _db.Products.Where(p => p.Category == cat).OrderByDescending(p => p.Stock).ToListAsync();
            return BuildAdminProductTable(products, $"Categoría: {CatLabel(cat)}");
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        private ChatbotResponse BuildProductGrid(List<Product> products, string title, string subtitle)
        {
            if (!products.Any())
                return Ok("<div class='dyp-chat-empty'><i class='bi bi-search'></i> No se encontraron productos.</div>");

            var sb = new StringBuilder();
            sb.Append($"<div class='dyp-chat-section-title'>{title}</div>");
            if (!string.IsNullOrEmpty(subtitle))
                sb.Append($"<div class='dyp-chat-section-sub'>{subtitle}</div>");
            sb.Append("<div class='dyp-chat-products'>");
            foreach (var p in products)
            {
                var catBadge = p.Category switch
                {
                    ProductCategory.boxing      => "🥊 Boxeo",
                    ProductCategory.shoes       => "👟 Tenis",
                    ProductCategory.supplements => "💊 Suplementos",
                    _ => ""
                };
                sb.Append($@"<a class='dyp-chat-product-card' href='/Products/Details/{p.Id}'>
                    {(p.ImageUrl != null ? $"<div class='dyp-chat-product-img' style='background-image:url(\"{p.ImageUrl}\")'></div>" : "<div class='dyp-chat-product-img dyp-chat-no-img'><i class='bi bi-bag'></i></div>")}
                    <div class='dyp-chat-product-info'>
                        <span class='dyp-chat-product-cat'>{catBadge}</span>
                        <span class='dyp-chat-product-name'>{p.Name}</span>
                        <span class='dyp-chat-product-price'>${p.Price:N0}</span>
                        <span class='dyp-chat-product-stock {(p.Stock > 0 ? "in-stock" : "out-stock")}'>{(p.Stock > 0 ? $"✅ {p.Stock} disp." : "❌ Agotado")}</span>
                    </div>
                </a>");
            }
            sb.Append("</div>");
            sb.Append("<a href='/Products' class='dyp-chat-link'>Ver toda la tienda →</a>");
            return Ok(sb.ToString());
        }

        private ChatbotResponse BuildAdminProductTable(List<Product> products, string title)
        {
            var sb = new StringBuilder();
            sb.Append($"<div class='dyp-chat-section-title'>{title} ({products.Count} productos)</div>");
            sb.Append("<div class='dyp-chat-table-wrap'><table class='dyp-chat-table'><thead><tr><th>Producto</th><th>Stock</th><th>Precio</th></tr></thead><tbody>");
            foreach (var p in products)
            {
                var sc = p.Stock == 0 ? "text-danger" : p.Stock <= 5 ? "text-warning" : "text-success";
                sb.Append($"<tr><td>{p.Name}</td><td class='{sc}'>{p.Stock}</td><td>${p.Price:N0}</td></tr>");
            }
            sb.Append("</tbody></table></div>");
            return Ok(sb.ToString());
        }

        private static string CatLabel(ProductCategory c) => c switch
        {
            ProductCategory.boxing      => "🥊 Boxeo",
            ProductCategory.shoes       => "👟 Tenis",
            ProductCategory.supplements => "💊 Suplementos",
            _ => c.ToString()
        };

        private static ChatbotResponse Ok(string html) => new() { Html = html, Text = StripHtml(html), IsError = false };
        private static ChatbotResponse Error(string msg) => new() { Html = $"<div class='dyp-chat-error'><i class='bi bi-exclamation-triangle-fill me-1'></i>{msg}</div>", Text = msg, IsError = true };

        private static bool ContainsAny(string haystack, params string[] needles)
            => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));

        private static string StripHtml(string html)
            => Regex.Replace(html, "<[^>]+>", " ").Trim();

        public static string RemoveDiacritics(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private async Task<string?> CallGeminiAsync(string systemPrompt, string userMessage, string apiKey)
        {
            try
            {
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={apiKey}";
                var payload = new
                {
                    systemInstruction = new
                    {
                        parts = new[] { new { text = systemPrompt } }
                    },
                    contents = new[]
                    {
                        new
                        {
                            role = "user",
                            parts = new[] { new { text = userMessage } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.7,
                        maxOutputTokens = 1000
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error: Status={Status}, Body={Body}", response.StatusCode, errText);
                    return null;
                }

                var resText = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resText);
                var root = doc.RootElement;
                if (root.TryGetProperty("candidates", out var candidates) &&
                    candidates.GetArrayLength() > 0 &&
                    candidates[0].TryGetProperty("content", out var contentObj) &&
                    contentObj.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textProp))
                {
                    return textProp.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API");
            }
            return null;
        }

        private async Task<string?> CallOpenAIAsync(string systemPrompt, string userMessage, string apiKey)
        {
            try
            {
                var url = "https://api.openai.com/v1/chat/completions";
                var payload = new
                {
                    model = "gpt-4o-mini",
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature = 0.7,
                    max_tokens = 1000
                };

                var json = JsonSerializer.Serialize(payload);
                var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    var errText = await response.Content.ReadAsStringAsync();
                    _logger.LogError("OpenAI API error: Status={Status}, Body={Body}", response.StatusCode, errText);
                    return null;
                }

                var resText = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(resText);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var messageObj) &&
                    messageObj.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling OpenAI API");
            }
            return null;
        }
    }
}
