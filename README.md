# Smart Retail Product Advisor

MCP-агент для пошуку, рекомендації та порівняння товарів. Система розуміє запити природною
мовою, зберігає вимоги користувача між ходами діалогу та отримує продуктові факти через
контрольовані MCP tools.

LLM використовується для визначення наміру й формування тексту відповіді. Ціни,
характеристики, availability, scores, ratings і checkout URL обчислюються або отримуються
детерміновано — модель не створює їх самостійно.

## Матеріали проєкту

- [Розгорнута система на Render](https://webapp-jcx5.onrender.com)
- [Miro: архітектура, call loop, пам'ять, контекст і презентація](https://miro.com/app/board/uXjVHz8Zh3A=/)
- [Prompt Book](prompt-book.md)
- [Специфікація](specs/001-smart-product-advisor/spec.md)
- [Архітектурний план](specs/001-smart-product-advisor/plan.md)
- [Модель даних](specs/001-smart-product-advisor/data-model.md)
- [API та MCP-контракти](specs/001-smart-product-advisor/contracts/)

## Компоненти

| Компонент | Призначення |
|---|---|
| `WebApp.Blazor` | Інтерфейс чату, пошуку, порівняння та перегляду товару |
| `Gateway.Api` | Автентифікований BFF і єдина API-точка входу для UI |
| `ProductAdvisor.Api` | Агент, conversation orchestration, MCP server і deterministic tools |
| `ProductCatalog.Api` | Товари, бренди, категорії, характеристики та параметричний пошук |
| `PricingAvailability.Api` | Актуальні ціни, знижки, availability і freshness timestamp |
| `PostgreSQL` | Окремі `catalogdb`, `pricingdb` та `advisordb` |
| `.NET Aspire` | Локальна оркестрація, service discovery та observability dashboard |

## Підключені джерела даних

| Джерело | Дані | Як використовується |
|---|---|---|
| Product Catalog | Назви, бренди, категорії та характеристики | Через внутрішній Catalog API і MCP read-only tools |
| Pricing & Availability | Ціни, знижки, стан запасів і `asOf` | Через внутрішній Pricing API та batch lookup |
| Advisor Database | Історія діалогу, `CurrentRequirement`, уточнення й останні результати | Структурована пам'ять агента |
| LLM provider | Structured intent extraction і narration | Через `Microsoft.Extensions.AI`; підтримується OpenAI-compatible endpoint |
| Google OAuth/OIDC | Ідентичність користувача | Вхід у WebApp та перевірка токена в Gateway |
| OTLP backend | Traces, metrics і logs | Опційний експорт через OpenTelemetry |

Демонстраційні Catalog і Pricing дані автоматично додаються під час локального запуску. Агент
працює лише з approved retailer data і не шукає продуктові факти на довільних вебсайтах.

## Вимоги

- [.NET SDK 10](https://dotnet.microsoft.com/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) або сумісний Docker Engine
- OpenAI-compatible LLM endpoint, API key і назва моделі
- Google OAuth 2.0 Client ID типу **Web application**

## Налаштування оточення

Створіть у корені локальний файл `.env`. Не додавайте його до Git.

```dotenv
LLM_PROVIDER_ENDPOINT=https://your-openai-compatible-endpoint
LLM_PROVIDER_API_KEY=replace-with-secret
LLM_PROVIDER_MODEL=your-model-name

INTERNAL_API_KEY=replace-with-long-random-secret

GOOGLE_CLIENT_ID=your-google-client-id
GOOGLE_CLIENT_SECRET=your-google-client-secret

# Опційно: зовнішній OpenTelemetry backend
OTEL_EXPORTER_OTLP_ENDPOINT=
OTEL_EXPORTER_OTLP_HEADERS=
OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf
```

Для Docker Compose додайте в Google Cloud Console redirect URI:

```text
http://localhost:5000/signin-google
```

### Основні змінні

| Змінна | Обов'язкова | Призначення |
|---|---:|---|
| `LLM_PROVIDER_ENDPOINT` | Так | OpenAI-compatible endpoint |
| `LLM_PROVIDER_API_KEY` | Так | Credential LLM provider |
| `LLM_PROVIDER_MODEL` | Так | Назва моделі |
| `INTERNAL_API_KEY` | Так | Автентифікація внутрішніх API та MCP endpoint |
| `GOOGLE_CLIENT_ID` | Так | Google OAuth client і перевірка audience |
| `GOOGLE_CLIENT_SECRET` | Так | Google sign-in у WebApp |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Ні | Адреса зовнішнього OTLP backend |
| `OTEL_EXPORTER_OTLP_HEADERS` | Ні | Authentication headers OTLP backend |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | Ні | Рекомендовано `http/protobuf` |

## Локальний запуск через Docker Compose

Це найкоротший відтворюваний спосіб запустити всю систему.

```powershell
docker compose up --build
```

Після успішного старту відкрийте:

```text
http://localhost:5000
```

Основні локальні endpoints:

| Сервіс | URL |
|---|---|
| WebApp | `http://localhost:5000` |
| Gateway | `http://localhost:5100` |
| Catalog API | `http://localhost:5101` |
| Pricing API | `http://localhost:5102` |
| Advisor / MCP | `http://localhost:5103` |
| PostgreSQL | `localhost:5432` |

Зупинка:

```powershell
docker compose down
```

## Локальний запуск через .NET Aspire

Для розробки з dashboard, traces, metrics і logs:

```powershell
$env:LlmProvider__Endpoint="https://your-openai-compatible-endpoint"
$env:LlmProvider__ApiKey="replace-with-secret"
$env:LlmProvider__Model="your-model-name"
$env:InternalApiKey="replace-with-long-random-secret"
$env:Authentication__Google__ClientId="your-google-client-id"
$env:Authentication__Google__ClientSecret="your-google-client-secret"

dotnet run --project src/Aspire/AppHost
```

Aspire Dashboard буде доступний за URL, показаним у terminal. Для Google OAuth зареєструйте
WebApp callback URL із суфіксом `/signin-google`; локальний HTTPS-профіль WebApp використовує
`https://localhost:7188`.

## Перевірка

Запуск автоматичних тестів:

```powershell
dotnet test src/ProductAdvisor.slnx
```

End-to-end тести потребують запущеного Docker Compose stack і налаштованого LLM provider:

```powershell
docker compose up --build -d
dotnet test tests/EndToEnd.Tests/EndToEnd.Tests.csproj
docker compose down
```

## Розгорнута система на Render

**Онлайн-версія:** [https://webapp-jcx5.onrender.com](https://webapp-jcx5.onrender.com)

Після відкриття система перенаправляє на Google для входу. Після авторизації дочекайтеся, поки
екран `Starting up…` перевірить Gateway, Advisor, Catalog і Pricing API, а потім введіть запит
природною мовою, наприклад: `Порадь ноутбук до 40 000 грн для навчання та програмування`.

Нюанси демонстраційного deployment:

- Усі п'ять компонентів працюють як окремі Render Free web services. Після 15 хвилин без
  вхідного трафіку вони зупиняються, а перший запит запускає їх знову. Render вказує, що один
  cold start зазвичай займає близько хвилини; через ланцюжок залежних сервісів перший запуск
  усієї системи може бути довшим.
- WebApp очікує готовність сервісів до чотирьох хвилин. Якщо частина системи ще запускається,
  інтерфейс відкриється у degraded mode; зачекайте та повторіть запит.
- Google OAuth є обов'язковим. У Google Cloud має бути зареєстрований callback
  `https://webapp-jcx5.onrender.com/signin-google`. Якщо OAuth consent screen працює в режимі
  `Testing`, Google-акаунт користувача потрібно додати до списку test users.
- Free web services не приймають private-network traffic, тому внутрішні API взаємодіють через
  публічні HTTPS hostnames. Вони захищені спільним `INTERNAL_API_KEY`; користувачеві потрібно
  відкривати лише WebApp URL.
- Локальна файлова система Render є ephemeral. Стан застосунку зберігається у зовнішніх
  PostgreSQL databases, тому connection strings, SSL і backups мають бути налаштовані окремо.
- Доступність відповіді також залежить від LLM provider і трьох databases. Безкоштовні інстанси
  мають спільні місячні ліміти workspace та не призначені для гарантованого production traffic.

Актуальні обмеження платформи описані в
[Render Free instances](https://render.com/docs/free) і
[Render Web Services](https://render.com/docs/web-services).

## Production deployment

Файл [render.yaml](render.yaml) описує хмарний deployment усіх п'яти сервісів у Render.
Production secrets задаються через dashboard платформи, а observability експортується в будь-який
OTLP-compatible backend.

Перед production deployment необхідно:

1. Задати окремий production `INTERNAL_API_KEY`.
2. Додати production callback URL у Google OAuth configuration.
3. Налаштувати LLM provider secrets.
4. Налаштувати encrypted PostgreSQL databases і backups.
5. За потреби задати OTLP endpoint та headers.
