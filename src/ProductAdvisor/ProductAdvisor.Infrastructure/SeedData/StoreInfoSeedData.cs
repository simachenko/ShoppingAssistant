using ProductAdvisor.Domain;

namespace ProductAdvisor.Infrastructure.SeedData;

/// <summary>
/// The demo store-policy knowledge base (002 research.md §12). This is validation/demo content —
/// it makes quickstart.md's scenarios runnable — not the production authoring path, which spec.md
/// 002's Assumptions place out of scope. Same convention as ProductCatalog/PricingAvailability's
/// own demo seed data: fixed guids, inserted only when seeding is enabled and the row is absent.
/// </summary>
public static class StoreInfoSeedData
{
    public static readonly Guid DeliveryDocumentId = Guid.Parse("00000000-0000-0000-0010-000000000001");
    public static readonly Guid PaymentDocumentId = Guid.Parse("00000000-0000-0000-0010-000000000002");
    public static readonly Guid ReturnsDocumentId = Guid.Parse("00000000-0000-0000-0010-000000000003");
    public static readonly Guid WarrantyDocumentId = Guid.Parse("00000000-0000-0000-0010-000000000004");
    public static readonly Guid LoyaltyDocumentId = Guid.Parse("00000000-0000-0000-0010-000000000005");
    public static readonly Guid ContactsDocumentId = Guid.Parse("00000000-0000-0000-0010-000000000006");

    // Ukrainian editions of the same policies. Separate Documents, not translated fields on the
    // English ones (spec.md 002 FR-031): each is independently retrievable, independently
    // versionable, and cited under its own title in the language the shopper actually read.
    public static readonly Guid DeliveryDocumentIdUk = Guid.Parse("00000000-0000-0000-0011-000000000001");
    public static readonly Guid PaymentDocumentIdUk = Guid.Parse("00000000-0000-0000-0011-000000000002");
    public static readonly Guid ReturnsDocumentIdUk = Guid.Parse("00000000-0000-0000-0011-000000000003");
    public static readonly Guid WarrantyDocumentIdUk = Guid.Parse("00000000-0000-0000-0011-000000000004");
    public static readonly Guid LoyaltyDocumentIdUk = Guid.Parse("00000000-0000-0000-0011-000000000005");
    public static readonly Guid ContactsDocumentIdUk = Guid.Parse("00000000-0000-0000-0011-000000000006");

    /// <summary>
    /// Content only — the embedding for each chunk is generated at seed time, since it depends on
    /// the deployment's configured embedding model (002 research.md §7) and cannot be a checked-in
    /// constant.
    /// </summary>
    public sealed record SeedDocument(
        Guid DocumentId,
        string Title,
        DocumentType DocumentType,
        string Language,
        IReadOnlyList<string> Chunks);

    public static IReadOnlyList<SeedDocument> Documents { get; } =
    [
        new(DeliveryDocumentId, "Delivery Terms", DocumentType.Delivery, "en",
        [
            "Standard delivery within Kyiv takes 1-2 business days. Delivery to other regions of Ukraine takes 2-5 business days, depending on the destination and the carrier's own schedule.",
            "Delivery is free for orders over 2000 UAH. For orders below that amount, a flat delivery fee of 90 UAH applies. Oversized items may carry an additional carrier surcharge, which is shown at checkout before payment.",
            "Orders placed before 14:00 on a business day are dispatched the same day. Orders placed after 14:00, at weekends, or on public holidays are dispatched on the next business day.",
        ]),

        new(PaymentDocumentId, "Payment Methods", DocumentType.Payment, "en",
        [
            "We accept Visa and Mastercard payment cards, Apple Pay, Google Pay, and bank transfer for corporate customers. Card payments are processed by our payment provider; we never store full card numbers ourselves.",
            "Cash on delivery is available for orders up to 20000 UAH, with a 2% service fee charged by the carrier. Payment in instalments is available for orders over 5000 UAH through our partner banks, for 3, 6, or 9 months.",
        ]),

        new(ReturnsDocumentId, "Returns and Exchanges", DocumentType.Returns, "en",
        [
            "You may return an unused product in its original packaging within 14 calendar days of receiving it, for a full refund. The 14-day period starts on the day the order is delivered to you.",
            "To start a return, contact our support team with your order number. We will arrange collection or provide a return shipping label. Refunds are issued to the original payment method within 7 business days of us receiving the returned item.",
            "Products that cannot be returned unless faulty include opened software, activated SIM cards, and personal hygiene items. A product with signs of use, missing accessories, or missing original packaging may be refused or refunded only in part.",
        ]),

        new(WarrantyDocumentId, "Warranty Terms", DocumentType.Warranty, "en",
        [
            "All products carry the manufacturer's warranty, which is 12 months for most items and 24 months for laptops and tablets. The warranty period starts on the date of purchase shown on your receipt.",
            "The warranty covers manufacturing defects and component failure under normal use. It does not cover physical damage, liquid damage, damage from unauthorised repair, or normal wear of consumable parts such as batteries below the manufacturer's stated capacity threshold.",
            "To make a warranty claim, bring or send the product with its receipt to our service centre. Diagnostics take up to 14 days; if a defect is confirmed, we repair or replace the product free of charge.",
        ]),

        new(LoyaltyDocumentId, "Loyalty Programme", DocumentType.Loyalty, "en",
        [
            "Our loyalty programme gives you 1 bonus point for every 10 UAH spent. Points are credited 14 days after delivery, once the return window has closed, and are valid for 12 months from the date they are credited.",
            "One bonus point is worth 1 UAH at checkout. You may pay for up to 30% of an order's value with bonus points. Points cannot be exchanged for cash and are not transferable between accounts.",
            "Membership tiers are Standard, Silver at 20000 UAH spent in a calendar year, and Gold at 50000 UAH. Silver members earn 1.5 points per 10 UAH and Gold members earn 2 points per 10 UAH.",
        ]),

        new(ContactsDocumentId, "Contact Us", DocumentType.Contacts, "en",
        [
            "Our support team is available by phone at 0 800 000 000, free of charge from within Ukraine, Monday to Friday from 09:00 to 20:00 and Saturday from 10:00 to 18:00. We are closed on Sundays and public holidays.",
            "You can email us at support@example-store.ua; we reply to email enquiries within one business day. Live chat is available on the website during the same hours as phone support.",
            "Our head office and service centre is at 1 Khreshchatyk Street, Kyiv, 01001, Ukraine. The service centre accepts warranty and repair drop-offs Monday to Friday from 10:00 to 18:00.",
        ]),

        // Ukrainian editions. Every figure below matches its English counterpart exactly — a
        // shopper must get the same policy regardless of which language they ask in, so a
        // divergence here would be a correctness bug, not a translation nuance.
        new(DeliveryDocumentIdUk, "Умови доставки", DocumentType.Delivery, "uk",
        [
            "Стандартна доставка в межах Києва триває 1-2 робочі дні. Доставка в інші регіони України триває 2-5 робочих днів залежно від населеного пункту та графіка перевізника.",
            "Доставка безкоштовна для замовлень від 2000 грн. Для замовлень на меншу суму діє фіксована вартість доставки 90 грн. Великогабаритні товари можуть мати додаткову надбавку перевізника, яка показується під час оформлення до оплати.",
            "Замовлення, оформлені до 14:00 у робочий день, відправляються того ж дня. Замовлення після 14:00, у вихідні або святкові дні відправляються наступного робочого дня.",
        ]),

        new(PaymentDocumentIdUk, "Способи оплати", DocumentType.Payment, "uk",
        [
            "Ми приймаємо платіжні картки Visa та Mastercard, Apple Pay, Google Pay, а також банківський переказ для корпоративних клієнтів. Оплату карткою обробляє наш платіжний провайдер; ми ніколи не зберігаємо повні номери карток у себе.",
            "Накладений платіж доступний для замовлень до 20000 грн із комісією перевізника 2%. Оплата частинами доступна для замовлень від 5000 грн через банки-партнери на 3, 6 або 9 місяців.",
        ]),

        new(ReturnsDocumentIdUk, "Повернення та обмін", DocumentType.Returns, "uk",
        [
            "Ви можете повернути невикористаний товар в оригінальній упаковці протягом 14 календарних днів з моменту отримання та отримати повне відшкодування. Відлік 14 днів починається з дня доставки замовлення.",
            "Щоб оформити повернення, зверніться до служби підтримки та вкажіть номер замовлення. Ми організуємо забір товару або надамо накладну для зворотної відправки. Кошти повертаються на початковий спосіб оплати протягом 7 робочих днів після отримання нами товару.",
            "Товари, які не підлягають поверненню без дефекту: відкрите програмне забезпечення, активовані SIM-картки та засоби особистої гігієни. Товар зі слідами використання, без комплектних аксесуарів або без оригінальної упаковки може бути не прийнятий або відшкодований частково.",
        ]),

        new(WarrantyDocumentIdUk, "Гарантійні умови", DocumentType.Warranty, "uk",
        [
            "На всі товари діє гарантія виробника: 12 місяців для більшості товарів і 24 місяці для ноутбуків та планшетів. Гарантійний строк починається з дати купівлі, зазначеної в чеку.",
            "Гарантія покриває виробничі дефекти та вихід з ладу компонентів за нормального використання. Вона не покриває механічні пошкодження, пошкодження рідиною, наслідки неавторизованого ремонту та природний знос витратних частин, зокрема акумуляторів нижче зазначеного виробником порога ємності.",
            "Щоб звернутися за гарантією, привезіть або надішліть товар разом із чеком до нашого сервісного центру. Діагностика триває до 14 днів; у разі підтвердження дефекту ми безкоштовно ремонтуємо або замінюємо товар.",
        ]),

        new(LoyaltyDocumentIdUk, "Програма лояльності", DocumentType.Loyalty, "uk",
        [
            "Програма лояльності нараховує 1 бонусний бал за кожні 10 грн покупки. Бали зараховуються через 14 днів після доставки, коли завершується строк повернення, і діють 12 місяців з дати нарахування.",
            "Один бонусний бал дорівнює 1 грн під час оформлення замовлення. Балами можна оплатити до 30% вартості замовлення. Бали не обмінюються на готівку і не передаються між акаунтами.",
            "Рівні участі: Standard, Silver — від 20000 грн витрат за календарний рік, і Gold — від 50000 грн. Учасники Silver отримують 1,5 бала за 10 грн, учасники Gold — 2 бали за 10 грн.",
        ]),

        new(ContactsDocumentIdUk, "Контакти", DocumentType.Contacts, "uk",
        [
            "Служба підтримки доступна за телефоном 0 800 000 000, безкоштовно в межах України, з понеділка по п'ятницю з 09:00 до 20:00 та в суботу з 10:00 до 18:00. У неділю та святкові дні ми не працюємо.",
            "Ви можете написати нам на support@example-store.ua; на листи ми відповідаємо протягом одного робочого дня. Онлайн-чат на сайті працює в ті самі години, що й телефонна підтримка.",
            "Головний офіс і сервісний центр розташовані за адресою: вул. Хрещатик, 1, Київ, 01001, Україна. Сервісний центр приймає товари на гарантію та ремонт з понеділка по п'ятницю з 10:00 до 18:00.",
        ]),
    ];
}
