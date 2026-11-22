// Проєкт №3 — ідемпотентні seed-дані ReviewsDb
// Запуск: mongosh "mongodb://localhost:27017" db/p3/seed.js
// Кожен документ вставляється через updateOne(..., {$setOnInsert}, {upsert:true}) за природним ключем —
// повторний запуск не створює дублікатів.

db = db.getSiblingDB("ReviewsDb");
const now = new Date();

function upsert(coll, filter, doc) {
  db[coll].updateOne(filter, { $setOnInsert: doc }, { upsert: true });
}

// --- reviews: різні типи документів (гнучка схема) ---
upsert("reviews", { "product.productId": NumberLong(1), "author.userId": "u-olena" }, {
  product: { productId: NumberLong(1), productName: "Кобзар" }, type: "text",
  author: { userId: "u-olena", displayName: "Олена К." },
  rating: { value: NumberInt(5) },
  title: "Must have", body: "Чудове видання, якісний папір.",
  comments: [
    { _id: ObjectId(), author: { userId: "u-taras", displayName: "Тарас" }, text: "Погоджуюсь!", createdAt: now }
  ],
  pros: [], cons: [],
  helpfulVotes: NumberInt(3), createdAt: now, updatedAt: null, version: NumberInt(1)
});

upsert("reviews", { "product.productId": NumberLong(3), "author.userId": "u-taras" }, {
  product: { productId: NumberLong(3), productName: "Clean Code" }, type: "pros-cons",
  author: { userId: "u-taras", displayName: "Тарас Б." },
  rating: { value: NumberInt(4) },
  title: "Класика для розробника",
  pros: ["Практичні приклади", "Зрозумілі правила"], cons: ["Приклади на Java"],
  comments: [], helpfulVotes: NumberInt(0), createdAt: now, updatedAt: null, version: NumberInt(1)
});

upsert("reviews", { "product.productId": NumberLong(4), "author.userId": "u-iryna" }, {
  product: { productId: NumberLong(4), productName: "Designing Data-Intensive Applications" }, type: "video",
  author: { userId: "u-iryna", displayName: "Ірина М." },
  rating: { value: NumberInt(5) },
  title: "Відеоогляд", videoUrl: "https://example.com/video/ddia-review",
  pros: [], cons: [],
  comments: [], helpfulVotes: NumberInt(1), createdAt: now, updatedAt: null, version: NumberInt(1)
});

// --- discussions (гібрид) + discussionMessages (посилання) ---
const topic = "Чи актуальна книга у 2026?";
upsert("discussions", { "product.productId": NumberLong(3), topic: topic }, {
  product: { productId: NumberLong(3), productName: "Clean Code" }, topic: topic,
  createdAt: now, lastActivityAt: now, messagesCount: NumberInt(2), version: NumberInt(1),
  recentMessages: [
    { author: { userId: "u-olena", displayName: "Олена К." }, text: "Так, принципи вічні", createdAt: now },
    { author: { userId: "u-taras", displayName: "Тарас Б." }, text: "Частково", createdAt: now }
  ]
});
const disc = db.discussions.findOne({ "product.productId": NumberLong(3), topic: topic });
upsert("discussionMessages", { discussionId: disc._id, text: "Так, принципи вічні" }, {
  discussionId: disc._id, parentMessageId: null,
  author: { userId: "u-olena", displayName: "Олена К." }, text: "Так, принципи вічні", createdAt: now
});
upsert("discussionMessages", { discussionId: disc._id, text: "Частково" }, {
  discussionId: disc._id, parentMessageId: null,
  author: { userId: "u-taras", displayName: "Тарас Б." }, text: "Частково", createdAt: now
});

// --- productRatings: матеріалізований підсумок ---
[[1, "Кобзар", 5], [3, "Clean Code", 4], [4, "Designing Data-Intensive Applications", 5]].forEach(([id, name, r]) =>
  upsert("productRatings", { _id: NumberLong(id) }, {
    _id: NumberLong(id), productName: name, reviewsCount: NumberInt(1), average: r * 1.0,
    distribution: { [String(r)]: NumberInt(1) }
  }));

print("ReviewsDb seeded");
