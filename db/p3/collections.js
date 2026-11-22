// Проєкт №3 — ReviewsDb (MongoDB), контекст «Відгуки та обговорення»
// Запуск: mongosh "mongodb://localhost:27017" db/p3/collections.js
// Скрипт ідемпотентний: колекції та індекси створюються лише якщо їх ще немає.

db = db.getSiblingDB("ReviewsDb");
const existing = db.getCollectionNames();

function ensureCollection(name, validator) {
  if (!existing.includes(name)) {
    db.createCollection(name, { validator, validationLevel: "moderate" });
    print(`created ${name}`);
  } else {
    db.runCommand({ collMod: name, validator, validationLevel: "moderate" });
  }
}

// 1) reviews — агрегат «Відгук»:
//    EMBEDDING: author (value object), rating, comments[] (коментарі завжди читаються разом із відгуком, їх мало)
//    REFERENCING: productId → CatalogDb.Products.Id (логічне посилання без FK)
//    ДЕНОРМАЛІЗАЦІЯ: productName — копія з Каталогу, оновлюється подією ProductUpdated (Лаб. 08)
//    ГНУЧКА СХЕМА: type = "text" | "pros-cons" | "video" — різні набори полів (pros/cons, videoUrl)
ensureCollection("reviews", {
  $jsonSchema: {
    bsonType: "object",
    required: ["productId", "productName", "author", "rating", "title", "createdAt", "version"],
    properties: {
      productId: { bsonType: "long" },
      productName: { bsonType: "string", maxLength: 200 },
      type: { enum: ["text", "pros-cons", "video"] },
      author: {
        bsonType: "object",
        required: ["userId", "displayName"],
        properties: { userId: { bsonType: "string" }, displayName: { bsonType: "string" } }
      },
      rating: {
        bsonType: "object",
        required: ["value"],
        properties: { value: { bsonType: "int", minimum: 1, maximum: 5 } }
      },
      title: { bsonType: "string", maxLength: 150 },
      body: { bsonType: "string", maxLength: 4000 },
      pros: { bsonType: "array", items: { bsonType: "string" } },
      cons: { bsonType: "array", items: { bsonType: "string" } },
      videoUrl: { bsonType: "string" },
      comments: { bsonType: "array" },
      helpfulVotes: { bsonType: "int", minimum: 0 },
      version: { bsonType: "int" }
    }
  }
});

// 2) discussions — ГІБРИДНИЙ підхід: останні 3 повідомлення вбудовані (recentMessages, швидкий показ),
//    повна історія — у окремій колекції discussionMessages (необмежений ріст → посилання)
ensureCollection("discussions", {
  $jsonSchema: {
    bsonType: "object",
    required: ["productId", "productName", "topic", "createdAt", "messagesCount"],
    properties: {
      productId: { bsonType: "long" },
      productName: { bsonType: "string" },
      topic: { bsonType: "string", maxLength: 200 },
      recentMessages: { bsonType: "array", maxItems: 3 },
      messagesCount: { bsonType: "int", minimum: 0 }
    }
  }
});

// 3) discussionMessages — REFERENCING: discussionId → discussions._id
ensureCollection("discussionMessages", {
  $jsonSchema: {
    bsonType: "object",
    required: ["discussionId", "author", "text", "createdAt"],
    properties: {
      discussionId: { bsonType: "objectId" },
      parentMessageId: { bsonType: ["objectId", "null"] },
      text: { bsonType: "string", maxLength: 2000 }
    }
  }
});

// 4) productRatings — матеріалізований підсумок рейтингу товару (оновлюється при створенні відгуку)
ensureCollection("productRatings", {
  $jsonSchema: {
    bsonType: "object",
    required: ["_id", "productName", "reviewsCount", "average"],
    properties: {
      _id: { bsonType: "long" },            // = productId
      reviewsCount: { bsonType: "int" },
      average: { bsonType: "double" },
      distribution: { bsonType: "object" }  // {"1": n, ..., "5": n}
    }
  }
});

// Індекси (createIndex ідемпотентний)
db.reviews.createIndex({ productId: 1, createdAt: -1 }, { name: "ix_reviews_product_created" });   // відгуки товару, новіші першими
db.reviews.createIndex({ productId: 1, "author.userId": 1 }, { name: "ux_reviews_product_author", unique: true }); // один відгук на товар від користувача
db.reviews.createIndex({ title: "text", body: "text" }, { name: "tx_reviews_text" });              // повнотекстовий пошук
db.discussions.createIndex({ productId: 1, lastActivityAt: -1 }, { name: "ix_discussions_product_activity" });
db.discussionMessages.createIndex({ discussionId: 1, createdAt: 1 }, { name: "ix_messages_discussion_created" });
print("ReviewsDb collections and indexes are ready");
